using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

namespace CemuPhoneGamePad;

internal sealed class PhoneServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Any, 26762);
    private readonly CancellationTokenSource stop = new();
    private readonly PadSource source;
    private readonly CemuWindow window;
    private readonly bool audio;
    private readonly Task worker;
    public string Code { get; } = RandomNumberGenerator.GetInt32(10000000, 100000000).ToString();
    public event Action<string>? Status;
    public PhoneServer(PadSource source, CemuWindow window, bool audio)
    {
        this.source = source; this.window = window; this.audio = audio;
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
                byte[] hello = new byte[12];
                using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                {
                    handshake.CancelAfter(TimeSpan.FromSeconds(5));
                    await stream.ReadExactlyAsync(hello, handshake.Token);
                }
                if (!hello.AsSpan(0, 4).SequenceEqual("PGP1"u8)
                    || !CryptographicOperations.FixedTimeEquals(hello.AsSpan(4), Encoding.ASCII.GetBytes(Code)))
                {
                    await Task.Delay(1000, stop.Token); continue;
                }
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
                    Task input = ReadControls(stream, session.Token);
                    Task video = StreamVideo(frames.Writer, session.Token);
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
    private async Task ReadControls(NetworkStream stream, CancellationToken token)
    {
        var bytes = new byte[80];
        while (!token.IsCancellationRequested)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await stream.ReadExactlyAsync(bytes, timeout.Token);
            source.Update(PadState.Parse(bytes));
        }
    }
    private async Task StreamVideo(ChannelWriter<(byte, byte[])> frames, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33));
        while (await timer.WaitForNextTickAsync(token)) frames.TryWrite((1, GamePadScreen.Capture(window)));
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
