using NAudio.Wave;
using NAudio.CoreAudioApi;
using NAudio.Wave.SampleProviders;

namespace CemuPhoneGamePad;

internal sealed class DesktopAudio : IDisposable
{
    private sealed class FastLoopback(MMDevice device) : WasapiCapture(device, false, 20)
    {
        protected override AudioClientStreamFlags GetAudioClientStreamFlags() => AudioClientStreamFlags.Loopback | base.GetAudioClientStreamFlags();
    }
    private readonly MMDevice endpoint = WasapiLoopbackCapture.GetDefaultLoopbackCaptureDevice();
    private readonly WasapiCapture capture;
    public DesktopAudio(Action<byte[]> output, Action<string> failed)
    {
        capture = new FastLoopback(endpoint);
        var buffer = new BufferedWaveProvider(capture.WaveFormat)
            { ReadFully = false, DiscardOnBufferOverflow = true, BufferDuration = TimeSpan.FromMilliseconds(60) };
        ISampleProvider samples = buffer.ToSampleProvider();
        if (samples.WaveFormat.Channels == 1) samples = new MonoToStereoSampleProvider(samples);
        else if (samples.WaveFormat.Channels != 2) samples = new MultiplexingSampleProvider([samples], 2);
        if (samples.WaveFormat.SampleRate != 48000) samples = new WdlResamplingSampleProvider(samples, 48000);
        var floats = new float[480];
        capture.DataAvailable += (_, data) =>
        {
            try
            {
                // Discard an old capture backlog rather than replaying it late.
                if (buffer.BufferedDuration.TotalMilliseconds > 30) buffer.ClearBuffer();
                int maximum = capture.WaveFormat.AverageBytesPerSecond / 25;
                maximum -= maximum % capture.WaveFormat.BlockAlign;
                int offset = Math.Max(0, data.BytesRecorded - maximum);
                buffer.AddSamples(data.Buffer, offset, data.BytesRecorded - offset);
                for (int block = 0; block < 16; block++)
                {
                    int count = samples.Read(floats, 0, floats.Length);
                    if (count == 0) break;
                    var pcm = new byte[count * 2];
                    for (int i = 0; i < count; i++)
                    {
                        short value = (short)Math.Clamp((int)(floats[i] * 32767), -32768, 32767);
                        pcm[i * 2] = (byte)value; pcm[i * 2 + 1] = (byte)(value >> 8);
                    }
                    output(pcm);
                }
            }
            catch (Exception e) { failed("Audio: " + e.Message); }
        };
        capture.RecordingStopped += (_, e) => { if (e.Exception != null) failed("Audio: " + e.Exception.Message); };
        try { capture.StartRecording(); } catch { capture.Dispose(); endpoint.Dispose(); throw; }
    }
    public void Dispose() { capture.StopRecording(); capture.Dispose(); endpoint.Dispose(); }
}
