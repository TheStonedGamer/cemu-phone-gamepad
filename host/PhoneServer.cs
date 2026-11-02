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
    private readonly PairingStore pairings;
    private sealed record PairRequest(string DeviceId, string Name, string? Code, string? Token);
    private sealed record AuthenticatedPhone(string? DeviceId, string? TokenHash);
    private readonly Task worker;
    public string Code { get; } = RandomNumberGenerator.GetInt32(10000000, 100000000).ToString();
    public event Action<string>? Status;
    public PhoneServer(PadSource source, CemuWindow window, bool audio, PairingStore pairings)
    {
        this.source = source; this.window = window; this.audio = audio; this.pairings = pairings;
        listener.Start(4); worker = Task.Run(Run);
    }
    private async Task Run()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var phone = await listener.AcceptTcpClientAsync(stop.Token);
                phone.NoDelay = true;
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
                try
                {
                    if (audio)
                    {
                        try { sound = new DesktopAudio(data => frames.Writer.TryWrite((2, data)), Message); }
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
                finally { sound?.Dispose(); source.Release(); session.Cancel(); }
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
                return new(request.DeviceId, hash);
            }
            await SendFrame(stream, 5, Encoding.UTF8.GetBytes("Saved pairing was forgotten or changed. Open SETUP and enter this PC's current code."), token);
            return null;
        }
        if (request.Code is not { Length: 8 } || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(request.Code), Encoding.ASCII.GetBytes(Code)))
        {
            await SendFrame(stream, 5, Encoding.UTF8.GetBytes("Pairing code was not accepted. Open SETUP and enter the host's current code."), token);
            return null;
        }
        var credential = pairings.Pair(request.DeviceId, request.Name ?? "Android phone");
        await SendFrame(stream, 4, JsonSerializer.SerializeToUtf8Bytes(new { saved = true, token = credential.Token }), token);
        return new(request.DeviceId, credential.Hash);
    }
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
        using var capture = new GamePadCapture(window);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33));
        string? notice = null;
        long nextHeartbeat = 0;
        while (await timer.WaitForNextTickAsync(token))
        {
            byte[]? image = await capture.NextImage(token);
            if (image != null) frames.TryWrite((1, image));
            string current = capture.Notice ?? "Streaming GamePad View — other windows may cover it";
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
