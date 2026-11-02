using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CemuPhoneGamePad;

internal sealed class DesktopAudio : IDisposable
{
    private readonly WasapiLoopbackCapture capture = new();
    public DesktopAudio(Action<byte[]> output, Action<string> failed)
    {
        var buffer = new BufferedWaveProvider(capture.WaveFormat)
            { ReadFully = false, DiscardOnBufferOverflow = true, BufferDuration = TimeSpan.FromMilliseconds(250) };
        ISampleProvider samples = buffer.ToSampleProvider();
        if (samples.WaveFormat.Channels == 1) samples = new MonoToStereoSampleProvider(samples);
        else if (samples.WaveFormat.Channels != 2) samples = new MultiplexingSampleProvider([samples], 2);
        if (samples.WaveFormat.SampleRate != 48000) samples = new WdlResamplingSampleProvider(samples, 48000);
        var floats = new float[1920];
        capture.DataAvailable += (_, data) =>
        {
            try
            {
                buffer.AddSamples(data.Buffer, 0, data.BytesRecorded);
                for (int block = 0; block < 8; block++)
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
        try { capture.StartRecording(); } catch { capture.Dispose(); throw; }
    }
    public void Dispose() { capture.StopRecording(); capture.Dispose(); }
}
