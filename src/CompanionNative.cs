using System.Runtime.InteropServices;
namespace BigBlueFish;

internal static class CompanionNative
{
    static readonly object gate = new();
    static readonly IntPtr display = XOpenDisplay(IntPtr.Zero);
    static readonly XErrorHandler errorHandler = (_, _) => 0;
    static CompanionNative() { if (display != IntPtr.Zero) XSetErrorHandler(errorHandler); }
    internal static ulong Root => display == IntPtr.Zero ? 0 : XDefaultRootWindow(display);
    static ulong Atom(string name) => XInternAtom(display, name, false);
    static byte[] Property(ulong window, string name, out int format)
    {
        format = 0;
        if (display == IntPtr.Zero) return [];
        if (XGetWindowProperty(display, window, Atom(name), 0, 4096, false, 0, out _, out format, out var count, out _, out var data) != 0 || data == IntPtr.Zero) return [];
        try { var bytes = new byte[checked((int)count * (format == 32 ? IntPtr.Size : Math.Max(1, format / 8)))]; Marshal.Copy(data, bytes, 0, bytes.Length); return bytes; }
        finally { XFree(data); }
    }
    static ulong[] Integers(ulong window, string property)
    {
        var bytes = Property(window, property, out var format); if (format != 32) return [];
        return Enumerable.Range(0, bytes.Length / IntPtr.Size).Select(i => IntPtr.Size == 8 ? BitConverter.ToUInt64(bytes, i * 8) : BitConverter.ToUInt32(bytes, i * 4)).ToArray();
    }
    internal static IntPtr GetForegroundWindow()
    {
        lock (gate) return display == IntPtr.Zero ? IntPtr.Zero : new IntPtr((long)(Integers(Root, "_NET_ACTIVE_WINDOW").FirstOrDefault()));
    }
    internal static bool IsFullscreen()
    {
        lock (gate)
        {
            var window = GetForegroundWindow();
            return window != IntPtr.Zero && Integers((ulong)window.ToInt64(), "_NET_WM_STATE").Contains(Atom("_NET_WM_STATE_FULLSCREEN"));
        }
    }
    internal static void SetInputShape(IntPtr window, byte[] pixels, int width, int height, double scale, bool mirror)
    {
        if (display == IntPtr.Zero || window == IntPtr.Zero) return;
        var regions = new List<XRectangle>();
        for (int y = 0; y < height; y++)
        {
            int start = -1;
            for (int x = 0; x <= width; x++)
            {
                bool opaque = x < width && pixels[(y * width + x) * 4 + 3] >= 8;
                if (opaque && start < 0) start = x;
                if (!opaque && start >= 0)
                {
                    int left = mirror ? width - x : start;
                    regions.Add(new() { x = (short)Math.Floor(left * scale), y = (short)Math.Floor(y * scale), width = (ushort)Math.Max(1, Math.Ceiling((x - start) * scale)), height = (ushort)Math.Max(1, Math.Ceiling(scale)) }); start = -1;
                }
            }
        }
        lock (gate) { XShapeCombineRectangles(display, (ulong)window.ToInt64(), 2, 0, 0, regions.ToArray(), regions.Count, 0, 0); XFlush(display); }
    }
    [StructLayout(LayoutKind.Sequential)] struct XRectangle { public short x, y; public ushort width, height; }
    [DllImport("libXext.so.6")] static extern void XShapeCombineRectangles(IntPtr display, ulong window, int kind, int x, int y, XRectangle[] rectangles, int count, int operation, int ordering);
    [DllImport("libX11.so.6")] static extern int XFlush(IntPtr display);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XErrorHandler(IntPtr display, IntPtr error);
    [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] static extern ulong XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] static extern ulong XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport("libX11.so.6")] static extern int XGetWindowProperty(IntPtr display, ulong window, ulong property, long offset, long length, bool delete, ulong requested, out ulong actual, out int format, out ulong count, out ulong rest, out IntPtr data);
    [DllImport("libX11.so.6")] static extern int XFree(IntPtr pointer);
    [DllImport("libX11.so.6")] static extern IntPtr XSetErrorHandler(XErrorHandler handler);
}
