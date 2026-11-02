using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;

namespace CemuPhoneGamePad;

// Windows Graphics Capture targets Cemu's composed window surface. It never
// samples the desktop and does not require Cemu to have keyboard focus.
internal sealed class GamePadCapture : IDisposable
{
    private readonly CemuWindow window;
    private readonly CaptureInterop.MtaLease mta = new();
    private IDirect3DDevice? device;
    private GraphicsCaptureItem? item;
    private Direct3D11CaptureFramePool? pool;
    private GraphicsCaptureSession? session;
    private SizeInt32 poolSize;
    private volatile bool closed;
    public string? Notice { get; private set; } = "Waiting for Cemu's GamePad image";

    public GamePadCapture(CemuWindow window)
    {
        this.window = window;
        try
        {
            GamePadScreen.EnsureOpen(window);
            if (!GraphicsCaptureSession.IsSupported())
                throw new NotSupportedException("Windows Graphics Capture is unavailable. Use Windows 10 2004 or newer with a supported graphics driver.");
            device = CaptureInterop.Device();
            item = CaptureInterop.ItemForWindow(window.Handle);
            item.Closed += OnClosed;
            poolSize = item.Size;
            CheckSize(poolSize);
            // FreeThreaded does not depend on the WinForms UI thread pumping
            // capture callbacks, including while stopping a phone session.
            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, poolSize);
            session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;
            session.StartCapture();
        }
        catch { Dispose(); throw; }
    }
    private void OnClosed(GraphicsCaptureItem sender, object args) => closed = true;
    private static void CheckSize(SizeInt32 size)
    {
        if (size.Width < 1 || size.Height < 1 || size.Width > 8192 || size.Height > 8192)
            throw new InvalidOperationException("Resize GamePad View to a usable size before starting capture");
    }
    public async Task<byte[]?> NextImage(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (closed) throw new InvalidOperationException("Cemu GamePad View was closed. Reopen it and restart the host.");
        GamePadScreen.EnsureOpen(window);
        if (GamePadScreen.IsMinimized(window))
        {
            Notice = "Restore GamePad View; it can stay behind other windows";
            return null;
        }
        Direct3D11CaptureFrame? frame = pool!.TryGetNextFrame();
        if (frame == null) return null;
        try
        {
            // Discard older queued frames; only read back the latest surface.
            for (int n = 0; n < 2; n++)
            {
                var newer = pool.TryGetNextFrame();
                if (newer == null) break;
                frame.Dispose(); frame = newer;
            }
            SizeInt32 size = frame.ContentSize;
            if (size.Width < 1 || size.Height < 1) return null;
            CheckSize(size);
            if (size.Width != poolSize.Width || size.Height != poolSize.Height)
            {
                // Return the checked-out surface before Recreate discards it.
                frame.Dispose(); frame = null;
                pool.Recreate(device!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
                poolSize = size;
                Notice = "Updating GamePad capture size";
                return null;
            }
            using var software = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface).AsTask(token);
            byte[] pixels;
            var buffer = new Windows.Storage.Streams.Buffer(checked((uint)(size.Width * size.Height * 4)));
            software.CopyToBuffer(buffer);
            CryptographicBuffer.CopyToByteArray(buffer, out pixels);
            Rectangle client = GamePadScreen.ClientAreaInCapture(window, size.Width, size.Height);
            if (client.Width < 1 || client.Height < 1) return null;
            // Strip the title/frame and Cemu's letterboxing before mapping the
            // image onto the phone's 16:9 touch-sensitive screen.
            int width = Math.Min(client.Width, (int)Math.Round(client.Height * 16.0 / 9));
            int height = Math.Min(client.Height, (int)Math.Round(client.Width * 9.0 / 16));
            var crop = new Rectangle(client.X + (client.Width - width) / 2, client.Y + (client.Height - height) / 2, width, height);
            using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
            var locked = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                int rowBytes = checked(size.Width * 4);
                for (int y = 0; y < size.Height; y++) Marshal.Copy(pixels, y * rowBytes, IntPtr.Add(locked.Scan0, y * locked.Stride), rowBytes);
            }
            finally { bitmap.UnlockBits(locked); }
            using var resized = new Bitmap(854, 480, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(resized))
            {
                graphics.InterpolationMode = InterpolationMode.Bilinear;
                graphics.DrawImage(bitmap, new Rectangle(0, 0, 854, 480), crop, GraphicsUnit.Pixel);
            }
            using var output = new MemoryStream();
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 70L);
            resized.Save(output, ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid), parameters);
            Notice = null;
            return output.ToArray();
        }
        finally { frame?.Dispose(); }
    }
    public void Dispose()
    {
        if (item != null) item.Closed -= OnClosed;
        session?.Dispose(); session = null;
        pool?.Dispose(); pool = null;
        device?.Dispose(); device = null;
        item = null; mta.Dispose();
    }
}
