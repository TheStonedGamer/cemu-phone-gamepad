using System.Diagnostics;
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
    public static bool IsMinimized(CemuWindow window) => IsIconic(window.Handle);
    public static void EnsureOpen(CemuWindow window)
    {
        GetWindowThreadProcessId(window.Handle, out uint pid);
        if (pid != window.ProcessId || !IsWindowVisible(window.Handle))
            throw new InvalidOperationException("Cemu GamePad View was closed. Reopen it and select the new window.");
    }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out Rect rect, int size);
    public static Rectangle ClientAreaInCapture(CemuWindow window, int captureWidth, int captureHeight)
    {
        EnsureOpen(window);
        var origin = new Point();
        if (!GetClientRect(window.Handle, out var client) || !ClientToScreen(window.Handle, ref origin))
            throw new InvalidOperationException("Cannot locate Cemu's GamePad image");
        Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(window.Handle, 9, out var bounds, Marshal.SizeOf<Rect>()));
        if (bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top) return Rectangle.Empty;
        float sx = captureWidth / (float)(bounds.Right - bounds.Left);
        float sy = captureHeight / (float)(bounds.Bottom - bounds.Top);
        int x = Math.Clamp((int)Math.Round((origin.X - bounds.Left) * sx), 0, captureWidth);
        int y = Math.Clamp((int)Math.Round((origin.Y - bounds.Top) * sy), 0, captureHeight);
        int right = Math.Clamp((int)Math.Round((origin.X - bounds.Left + client.Right - client.Left) * sx), x, captureWidth);
        int bottom = Math.Clamp((int)Math.Round((origin.Y - bounds.Top + client.Bottom - client.Top) * sy), y, captureHeight);
        return Rectangle.FromLTRB(x, y, right, bottom);
    }
}
