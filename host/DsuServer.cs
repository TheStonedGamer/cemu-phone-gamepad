using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace CemuPhoneGamePad;

// DSU/Cemuhook protocol 1001. Cemu consumes buttons, axes, motion AND touch
// directly, so the PC does not need a virtual gamepad driver or mouse injection.
internal sealed class DsuServer : IDisposable
{
    private readonly UdpClient udp;
    private readonly PadSource source;
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private readonly uint identity = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
    private readonly Dictionary<IPEndPoint, long> subscribers = new();
    private uint counter;
    private static readonly byte[] Mac = [0x02, 0x43, 0x50, 0x47, 0x00, 0x01];
    public event Action<string>? Failed;
    public event Action<string>? Status;
    private string lastStatus = "";
    public DsuServer(PadSource source)
    {
        this.source = source;
        udp = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            udp.Client.ExclusiveAddressUse = true;
            udp.Client.Bind(new IPEndPoint(IPAddress.Loopback, 26760));
            if (OperatingSystem.IsWindows())
            {
                // Closed Cemu UDP sockets can produce ICMP port-unreachable
                // notifications. They must not shut down the DSU service.
                try { udp.Client.IOControl((IOControlCode)unchecked((int)0x9800000C), new byte[4], null); }
                catch (SocketException) { }
            }
        }
        catch { udp.Dispose(); throw; }
        worker = Task.Run(Run);
    }
    private void Report(string value) { if (lastStatus != value) { lastStatus = value; Status?.Invoke(value); } }
    private static bool PeerError(SocketException e) => e.SocketErrorCode is SocketError.ConnectionReset
        or SocketError.ConnectionRefused or SocketError.HostUnreachable or SocketError.NetworkUnreachable;
    private async Task Run()
    {
        try
        {
            var pending = udp.ReceiveAsync(stop.Token).AsTask();
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16));
            while (await timer.WaitForNextTickAsync(stop.Token))
            {
                for (int n = 0; n < 32 && pending.IsCompleted; n++)
                {
                    try { Handle(await pending); }
                    catch (SocketException e) when (PeerError(e)) { }
                    pending = udp.ReceiveAsync(stop.Token).AsTask();
                }
                long now = Environment.TickCount64;
                foreach (var peer in subscribers.Keys.ToArray())
                {
                    if (now - subscribers[peer] > 5000) { subscribers.Remove(peer); continue; }
                    try { udp.Send(Data(), peer); }
                    catch (SocketException e) when (PeerError(e)) { subscribers.Remove(peer); }
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception e) { Failed?.Invoke(e.Message); }
    }
    private void Handle(UdpReceiveResult packet)
    {
        var p = packet.Buffer;
        if (p.Length < 20 || p.Length > 128 || !p.AsSpan(0, 4).SequenceEqual("DSUC"u8)
            || BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(4)) > 1001
            || BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(6)) != p.Length - 16) return;
        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(8));
        p.AsSpan(8, 4).Clear();
        if (Crc(p) != crc) return;
        uint type = BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(16));
        if (type == 0x100000 && p.Length == 20)
        {
            var reply = Message(type, 24);
            BinaryPrimitives.WriteUInt16LittleEndian(reply.AsSpan(20), 1001);
            Send(reply, packet.RemoteEndPoint);
        }
        else if (type == 0x100001 && p.Length == 28)
        {
            Report("Controller list requested — Controller 1 is available");
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(20));
            if (count > 4) return;
            for (int i = 0; i < count; i++) if (p[24 + i] < 4)
            {
                var reply = Message(type, 32); Info(reply, p[24 + i]); Send(reply, packet.RemoteEndPoint);
            }
        }
        else if (type == 0x100002 && p.Length == 28 && (p[20] & ~3) == 0)
        {
            bool match = p[20] == 0 || ((p[20] & 1) != 0 && p[21] == 0)
                || ((p[20] & 2) != 0 && p.AsSpan(22, 6).SequenceEqual(Mac));
            if (match && (subscribers.Count < 8 || subscribers.ContainsKey(packet.RemoteEndPoint)))
            {
                subscribers[packet.RemoteEndPoint] = Environment.TickCount64;
                Report("Controller data requested by the emulator");
            }
        }
    }
    private byte[] Message(uint type, int size)
    {
        var p = new byte[size]; "DSUS"u8.CopyTo(p);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4), 1001);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6), (ushort)(size - 16));
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(12), identity);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(16), type);
        return p;
    }
    private void Info(byte[] p, byte index)
    {
        p[20] = index;
        // This is a persistent virtual GamePad. Phone freshness controls its
        // values, not whether Cemu is allowed to enumerate it during setup.
        bool connected = index == 0;
        p[21] = (byte)(connected ? 2 : 0); p[22] = (byte)(connected ? 2 : 0); p[23] = (byte)(connected ? 2 : 0);
        if (index == 0) Mac.CopyTo(p, 24);
        p[30] = (byte)(connected ? 5 : 0);
    }
    private byte[] Data()
    {
        var p = Message(0x100002, 100); Info(p, 0);
        var (s, _) = source.Snapshot();
        p[31] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(32), ++counter);
        p[36] = (byte)s.Buttons; p[37] = (byte)(s.Buttons >> 8); p[38] = (byte)((s.Buttons >> 16) & 1); p[39] = p[38];
        static byte Axis(float value) => (byte)Math.Clamp((int)Math.Round((value + 1) * 127.5), 0, 255);
        p[40] = Axis(s.LX); p[41] = Axis(-s.LY); p[42] = Axis(s.RX); p[43] = Axis(-s.RY);
        int[] bits = [7, 6, 5, 4, 15, 14, 13, 12, 11, 10, 9, 8];
        for (int i = 0; i < bits.Length; i++) p[44 + i] = (byte)((s.Buttons & (1U << bits[i])) != 0 ? 255 : 0);
        p[56] = (byte)(s.Touch ? 1 : 0); p[57] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(58), (ushort)Math.Round(s.TX * 1920));
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(60), (ushort)Math.Round(s.TY * 942));
        BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(68), PadSource.Timestamp);
        float[] motion = [s.AX, s.AY, s.AZ, s.Pitch, s.Yaw, s.Roll];
        for (int i = 0; i < motion.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(76 + 4 * i), motion[i]);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(8), Crc(p));
        return p;
    }
    private void Send(byte[] p, IPEndPoint peer) { BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(8), Crc(p)); udp.Send(p, peer); }
    private static uint Crc(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data) { crc ^= b; for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320U : 0); }
        return ~crc;
    }
    public void Dispose()
    {
        source.Release(); stop.Cancel(); worker.GetAwaiter().GetResult();
        // Cemu caches the last DSU state. Send a final release before closing
        // the socket so exiting the app cannot leave a held phone button.
        foreach (var peer in subscribers.Keys)
        {
            try
            {
                udp.Send(Data(), peer);
                var disconnected = Message(0x100001, 32); Info(disconnected, 0);
                disconnected[21] = disconnected[22] = disconnected[23] = disconnected[30] = 0;
                Send(disconnected, peer);
            }
            catch (SocketException) { }
        }
        udp.Dispose(); stop.Dispose();
    }
}
