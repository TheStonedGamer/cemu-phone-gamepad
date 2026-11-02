using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using System.Text.Json;

namespace CemuPhoneGamePad;

internal sealed class PhoneServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Any, 26762);
    private readonly CancellationTokenSource stop = new();
    private readonly PadSource source;
    private readonly CemuWindow window;
    private readonly bool audio;
    private readonly int framesPerSecond, quality;
    private readonly PairingStore pairings;
    private sealed record PairRequest(string DeviceId, string Name, string? Code, string? Token, int AudioPort = 0);
    private sealed record AuthenticatedPhone(string? DeviceId, string? TokenHash, int AudioPort = 0);
    private readonly Task worker;
    public string Code { get; } = RandomNumberGenerator.GetInt32(10000000, 100000000).ToString();
    public event Action<string>? Status;
    public PhoneServer(PadSource source, CemuWindow window, bool audio, PairingStore pairings, int framesPerSecond = 30, int quality = 70)
    {
        this.source = source; this.window = window; this.audio = audio; this.pairings = pairings;
        this.framesPerSecond = framesPerSecond == 15 ? 15 : 30; this.quality = quality;
        listener.Start(4); worker = Task.Run(Run);
    }
    private async Task Run()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var phone = await listener.AcceptTcpClientAsync(stop.Token);
                phone.NoDelay = true; phone.SendBufferSize = 65536;
                using var session = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                var stream = phone.GetStream();
                AuthenticatedPhone? trusted;
                using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                {
                    handshake.CancelAfter(TimeSpan.FromSeconds(5));
                    trusted = await Authenticate(stream, handshake.Token);
                }
                if (trusted == null) { await Task.Delay(1000, stop.Token); continue; }
                Status?.Invoke("Android connected — streaming GamePad View");
                var frames = Channel.CreateBounded<(byte Type, byte[] Data)>(new BoundedChannelOptions(8)
                    { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
                void Message(string message)
                {
                    Status?.Invoke(message);
                    frames.Writer.TryWrite((3, Encoding.UTF8.GetBytes(message)));
                }
                DesktopAudio? sound = null;
                UdpClient? audioSocket = null;
                try
                {
                    if (audio)
                    {
                        try
                        {
                            if (trusted.AudioPort != 0 && phone.Client.RemoteEndPoint is IPEndPoint remote)
                            {
                                audioSocket = new UdpClient(AddressFamily.InterNetwork);
                                audioSocket.Client.SendBufferSize = 8192;
                                audioSocket.Connect(remote.Address, trusted.AudioPort);
                                audioSocket.Client.Blocking = false;
                                byte[] audioId = RandomNumberGenerator.GetBytes(16);
                                await SendFrame(stream, 6, JsonSerializer.SerializeToUtf8Bytes(new { transport = "udp", session = Convert.ToHexString(audioId), sampleRate = 48000, channels = 2 }), session.Token);
                                uint sequence = 0;
                                var sender = audioSocket;
                                sound = new DesktopAudio(data =>
                                {
                                    // Five milliseconds of stereo PCM fits below a LAN MTU.
                                    var packet = new byte[32 + data.Length]; "PGPA"u8.CopyTo(packet);
                                    audioId.CopyTo(packet, 4);
                                    BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(20), sequence++);
                                    BinaryPrimitives.WriteInt64BigEndian(packet.AsSpan(24), Environment.TickCount64);
                                    data.CopyTo(packet, 32);
                                    try { sender.Send(packet); } catch (SocketException) { }
                                }, Message);
                            }
                            else sound = new DesktopAudio(data => frames.Writer.TryWrite((2, data)), Message);
                        }
                        catch (Exception e) { Message("Audio unavailable: " + e.Message); }
                    }
                    Task input = ReadControls(stream, trusted, session.Token);
                    Task video = StreamVideo(frames.Writer, Message, session.Token);
                    Task output = WriteFrames(stream, frames.Reader, session.Token);
                    Task finished = await Task.WhenAny(input, video, output);
                    session.Cancel(); phone.Close();
                    try { await finished; } finally
                    {
                        session.Cancel(); frames.Writer.TryComplete();
                        try { await Task.WhenAll(input, video, output); } catch { }
                    }
                }
                finally { sound?.Dispose(); audioSocket?.Dispose(); source.Release(); session.Cancel(); }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                source.Release(); Status?.Invoke("Phone disconnected: " + e.Message);
            }
        }
    }
    private async Task<AuthenticatedPhone?> Authenticate(NetworkStream stream, CancellationToken token)
    {
        var magic = new byte[4];
        await stream.ReadExactlyAsync(magic, token);
        if (magic.AsSpan().SequenceEqual("PGP1"u8))
        {
            // Older phone builds remain usable for this host session only.
            var code = new byte[8]; await stream.ReadExactlyAsync(code, token);
            return CryptographicOperations.FixedTimeEquals(code, Encoding.ASCII.GetBytes(Code)) ? new(null, null) : null;
        }
        if (!magic.AsSpan().SequenceEqual("PGP2"u8)) return null;
        var lengthBytes = new byte[4]; await stream.ReadExactlyAsync(lengthBytes, token);
        int length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
        if (length < 2 || length > 1024) return null;
        var payload = new byte[length]; await stream.ReadExactlyAsync(payload, token);
        PairRequest? request;
        try { request = JsonSerializer.Deserialize<PairRequest>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 8 }); }
        catch (JsonException) { return null; }
        if (request == null || !Guid.TryParseExact(request.DeviceId, "N", out _)) return null;
        if (!string.IsNullOrEmpty(request.Token))
        {
            if (pairings.Authenticate(request.DeviceId, request.Token, out string hash))
            {
                await SendFrame(stream, 4, JsonSerializer.SerializeToUtf8Bytes(new { saved = true }), token);
                return new(request.DeviceId, hash, ValidAudioPort(request.AudioPort));
            }
            await SendFrame(stream, 5, Encoding.UTF8.GetBytes("Saved pairing was forgotten or changed. Open Menu → Connection and enter this PC's current code."), token);
            return null;
        }
        if (request.Code is not { Length: 8 } || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(request.Code), Encoding.ASCII.GetBytes(Code)))
        {
            await SendFrame(stream, 5, Encoding.UTF8.GetBytes("Pairing code was not accepted. Open Menu → Connection and enter the host's current code."), token);
            return null;
        }
        var credential = pairings.Pair(request.DeviceId, request.Name ?? "Android phone");
        await SendFrame(stream, 4, JsonSerializer.SerializeToUtf8Bytes(new { saved = true, token = credential.Token }), token);
        return new(request.DeviceId, credential.Hash, ValidAudioPort(request.AudioPort));
    }
    private static int ValidAudioPort(int port) => port is >= 1024 and <= 65535 ? port : 0;
    private static async Task SendFrame(NetworkStream stream, byte type, byte[] payload, CancellationToken token)
    {
        var header = new byte[5]; header[0] = type;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1), payload.Length);
        await stream.WriteAsync(header, token); await stream.WriteAsync(payload, token);
    }
    private async Task ReadControls(NetworkStream stream, AuthenticatedPhone trusted, CancellationToken token)
    {
        var bytes = new byte[80];
        while (!token.IsCancellationRequested)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await stream.ReadExactlyAsync(bytes, timeout.Token);
            if (trusted.DeviceId != null && !pairings.Authorized(trusted.DeviceId, trusted.TokenHash!))
                throw new InvalidOperationException("This phone was forgotten. Enter a pairing code to connect again.");
            source.Update(PadState.Parse(bytes));
        }
    }
    private async Task StreamVideo(ChannelWriter<(byte, byte[])> frames, Action<string> message, CancellationToken token)
    {
        GamePadCapture? capture = null;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000.0 / framesPerSecond));
        string? notice = null;
        long nextHeartbeat = 0, retryAt = 0;
        string current = "Waiting for GamePad video";
        try
        {
        while (await timer.WaitForNextTickAsync(token))
        {
            try
            {
                if (capture == null && Environment.TickCount64 >= retryAt) capture = new GamePadCapture(window, quality);
                if (capture != null)
                {
                    byte[]? image = await capture.NextImage(token);
                    if (image != null) frames.TryWrite((1, image));
                    current = capture.Notice ?? "Streaming GamePad View";
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                capture?.Dispose(); capture = null; retryAt = Environment.TickCount64 + 5000;
                current = "Video paused: " + e.Message + " Controls remain connected. Stop and select a new window if Cemu was restarted.";
            }
            if (current != notice) { notice = current; message(current); }
            if (Environment.TickCount64 >= nextHeartbeat)
            {
                // A static or minimized capture may produce no video frames.
                // Keep the phone's transport alive without duplicating images.
                frames.TryWrite((3, Encoding.UTF8.GetBytes(current)));
                nextHeartbeat = Environment.TickCount64 + 1000;
            }
        }
        }
        finally { capture?.Dispose(); }
    }
    private static async Task WriteFrames(NetworkStream stream, ChannelReader<(byte Type, byte[] Data)> frames, CancellationToken token)
    {
        await foreach (var frame in frames.ReadAllAsync(token))
        {
            var header = new byte[5]; header[0] = frame.Type;
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1), frame.Data.Length);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await stream.WriteAsync(header, timeout.Token);
            await stream.WriteAsync(frame.Data, timeout.Token);
        }
    }
    public void Dispose()
    {
        stop.Cancel(); listener.Stop(); worker.GetAwaiter().GetResult(); source.Release(); stop.Dispose();
    }
}
