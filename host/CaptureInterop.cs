using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace CemuPhoneGamePad;

// HWND and DXGI interop from the public Windows SDK interfaces. Raw COM
// references are scoped here; the rest of the app uses CsWinRT projections.
internal static class CaptureInterop
{
    [DllImport("api-ms-win-core-com-l1-1-0.dll")] private static extern int CoIncrementMTAUsage(out IntPtr cookie);
    [DllImport("api-ms-win-core-com-l1-1-0.dll")] private static extern int CoDecrementMTAUsage(IntPtr cookie);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string value, uint length, out IntPtr handle);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr handle);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr className, ref Guid iid, out IntPtr factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(IntPtr adapter, uint driverType,
        IntPtr software, uint flags, IntPtr featureLevels, uint levelCount, uint sdkVersion,
        out IntPtr device, out uint selectedLevel, out IntPtr context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr inspectable);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateForWindow(IntPtr factory, IntPtr window, ref Guid iid, out IntPtr item);

    internal sealed class MtaLease : IDisposable
    {
        private IntPtr cookie;
        public MtaLease() => Marshal.ThrowExceptionForHR(CoIncrementMTAUsage(out cookie));
        public void Dispose() { var old = Interlocked.Exchange(ref cookie, IntPtr.Zero); if (old != IntPtr.Zero) CoDecrementMTAUsage(old); }
    }

    public static GraphicsCaptureItem ItemForWindow(IntPtr window)
    {
        const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Guid interopId = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
        Guid itemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        IntPtr className = IntPtr.Zero, factory = IntPtr.Zero, item = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(name, (uint)name.Length, out className));
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, ref interopId, out factory));
            // IGraphicsCaptureItemInterop inherits IUnknown; CreateForWindow is
            // the first method following its three IUnknown vtable entries.
            var function = Marshal.GetDelegateForFunctionPointer<CreateForWindow>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 3 * IntPtr.Size));
            Marshal.ThrowExceptionForHR(function(factory, window, ref itemId, out item));
            return WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(item);
        }
        finally
        {
            if (item != IntPtr.Zero) Marshal.Release(item);
            if (factory != IntPtr.Zero) Marshal.Release(factory);
            if (className != IntPtr.Zero) WindowsDeleteString(className);
        }
    }

    public static IDirect3DDevice Device()
    {
        IntPtr native = IntPtr.Zero, context = IntPtr.Zero, dxgi = IntPtr.Zero, inspectable = IntPtr.Zero;
        try
        {
            int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out native, out _, out context);
            if (hr < 0)
            {
                if (native != IntPtr.Zero) { Marshal.Release(native); native = IntPtr.Zero; }
                if (context != IntPtr.Zero) { Marshal.Release(context); context = IntPtr.Zero; }
                Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, 5, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out native, out _, out context));
            }
            Guid dxgiId = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(native, in dxgiId, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            if (inspectable != IntPtr.Zero) Marshal.Release(inspectable);
            if (dxgi != IntPtr.Zero) Marshal.Release(dxgi);
            if (context != IntPtr.Zero) Marshal.Release(context);
            if (native != IntPtr.Zero) Marshal.Release(native);
        }
    }
}
