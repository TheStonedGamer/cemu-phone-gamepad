using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace CemuPhoneGamePad;

internal sealed record CemuWindow(IntPtr Handle, uint ProcessId, string Title)
{
    public override string ToString() => Title;
}

internal static class GamePadScreen
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    private delegate bool EnumProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder title, int size);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    public static CemuWindow[] Windows()
    {
        var result = new List<CemuWindow>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            GetWindowThreadProcessId(window, out uint pid);
            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (!process.ProcessName.Equals("Cemu", StringComparison.OrdinalIgnoreCase)) return true;
                var title = new StringBuilder(512); GetWindowText(window, title, 512);
                if (title.Length != 0) result.Add(new(window, pid, title.ToString()));
            }
            catch (ArgumentException) { }
            return true;
        }, IntPtr.Zero);
        return result.OrderByDescending(w => w.Title.Contains("GamePad", StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    public static byte[] Capture(CemuWindow window)
    {
        GetWindowThreadProcessId(window.Handle, out uint pid);
        if (pid != window.ProcessId || !IsWindowVisible(window.Handle) || IsIconic(window.Handle)
            || !GetClientRect(window.Handle, out var rect))
            throw new InvalidOperationException("Cemu GamePad View was closed or minimized. Keep it visible.");
        var origin = new Point();
        if (!ClientToScreen(window.Handle, ref origin)) throw new InvalidOperationException("Cannot locate GamePad View");
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        if (width < 160 || height < 90 || width > 8192 || height > 8192) throw new InvalidOperationException("Resize GamePad View to a usable size");
        // Cemu letterboxes the 16:9 GamePad image. Crop those bars so touch
        // coordinates on the phone refer to the displayed image, not the frame.
        int cropWidth = Math.Min(width, (int)Math.Round(height * 16.0 / 9));
        int cropHeight = Math.Min(height, (int)Math.Round(width * 9.0 / 16));
        using var capture = new Bitmap(cropWidth, cropHeight, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(capture))
            graphics.CopyFromScreen(origin.X + (width - cropWidth) / 2, origin.Y + (height - cropHeight) / 2,
                0, 0, new Size(cropWidth, cropHeight), CopyPixelOperation.SourceCopy);
        using var resized = new Bitmap(854, 480, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            graphics.DrawImage(capture, 0, 0, 854, 480);
        }
        using var stream = new MemoryStream();
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 70L);
        resized.Save(stream, ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid), parameters);
        return stream.ToArray();
    }
}
