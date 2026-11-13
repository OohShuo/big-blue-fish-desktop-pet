using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

namespace BigBlueFish;

internal sealed record CompanionWindowImage(string DataUrl, int Width, int Height);

internal static partial class CompanionNative
{
    internal static IntPtr ResolveForeground(IntPtr current, IntPtr previous)
    {
        lock (gate)
        {
            bool Valid(IntPtr handle)
            {
                if (handle == IntPtr.Zero || display == IntPtr.Zero) return false;
                ulong window = (ulong)handle.ToInt64();
                return XGetWindowAttributes(display, window, out var attributes) != 0 && attributes.map_state == 2
                    && !Integers(window, "_NET_WM_STATE").Contains(Atom("_NET_WM_STATE_HIDDEN"))
                    && Integers(window, "_NET_WM_PID").FirstOrDefault() != (ulong)Environment.ProcessId;
            }
            if (Valid(current)) return current;
            if (Valid(previous)) return previous;
            return new IntPtr((long)Root);
        }
    }

    internal static string ForegroundIdentity(IntPtr handle)
    {
        lock (gate)
        {
            if (handle == IntPtr.Zero || (ulong)handle.ToInt64() == Root) return "前台窗口：Linux 桌面\n画面是壁纸与快捷方式。";
            var title = Encoding.UTF8.GetString(Property((ulong)handle.ToInt64(), "_NET_WM_NAME", out _)).TrimEnd('\0');
            if (title.Length == 0) title = Encoding.UTF8.GetString(Property((ulong)handle.ToInt64(), "WM_NAME", out _)).TrimEnd('\0');
            var application = Encoding.UTF8.GetString(Property((ulong)handle.ToInt64(), "WM_CLASS", out _)).Replace('\0', ' ').Trim();
            return $"前台应用：{application}\n窗口标题：{title}";
        }
    }

    internal static CompanionWindowImage Capture(IntPtr handle, int maximum, bool full)
    {
        lock (gate)
        {
            if (display == IntPtr.Zero) throw new IOException("当前桌面未提供 X11 读屏连接。");
            if (XGetWindowAttributes(display, Root, out var screen) == 0) throw new IOException("无法读取桌面尺寸。");
            int x = 0, y = 0, width = screen.width, height = screen.height, windowX = 0, windowY = 0;
            bool application = !full && handle != IntPtr.Zero && (ulong)handle.ToInt64() != Root;
            if (application)
            {
                if (XGetWindowAttributes(display, (ulong)handle.ToInt64(), out var window) == 0 || window.map_state != 2) throw new IOException("前台窗口不可见。");
                XTranslateCoordinates(display, (ulong)handle.ToInt64(), Root, 0, 0, out x, out y, out _);
                windowX = x; windowY = y;
                width = Math.Min(window.width + Math.Min(0, x), screen.width - Math.Max(0, x));
                height = Math.Min(window.height + Math.Min(0, y), screen.height - Math.Max(0, y)); x = Math.Max(0, x); y = Math.Max(0, y);
            }
            if (width <= 0 || height <= 0 || (long)width * height > 150_000_000) throw new IOException("截图区域无效。");
            ulong pixmap = 0; bool redirected = false;
            IntPtr pointer;
            try
            {
                if (application)
                {
                    ulong drawable = (ulong)handle.ToInt64(); int sourceX = windowX, sourceY = windowY, border = 0;
                    if (XGetSelectionOwner(display, Atom("_NET_WM_CM_S0")) == 0)
                    {
                        XCompositeRedirectWindow(display, drawable, 0); redirected = true; XSync(display, false);
                    }
                    else
                    {
                        for (int depth = 0; depth < 32; depth++)
                        {
                            if (XQueryTree(display, drawable, out _, out var parent, out var children, out _) == 0) break;
                            if (children != IntPtr.Zero) XFree(children);
                            if (parent == 0 || parent == Root) break;
                            drawable = parent;
                        }
                        XTranslateCoordinates(display, drawable, Root, 0, 0, out sourceX, out sourceY, out _);
                        if (XGetWindowAttributes(display, drawable, out var frame) != 0) border = frame.border;
                    }
                    pixmap = XCompositeNameWindowPixmap(display, drawable);
                    pointer = XGetImage(display, pixmap, x - sourceX + border, y - sourceY + border, (uint)width, (uint)height, ulong.MaxValue, 2);
                }
                else pointer = XGetImage(display, Root, x, y, (uint)width, (uint)height, ulong.MaxValue, 2);
            }
            finally
            {
                if (pixmap != 0) XFreePixmap(display, pixmap);
                if (redirected) XCompositeUnredirectWindow(display, (ulong)handle.ToInt64(), 0);
            }
            if (pointer == IntPtr.Zero) throw new IOException("无法读取桌面像素。");
            try
            {
                var image = Marshal.PtrToStructure<XImage>(pointer);
                if (image.bits_per_pixel != 32 || image.byte_order != 0) throw new IOException("当前桌面像素格式不受支持。");
                var pixels = new byte[image.bytes_per_line * height]; Marshal.Copy(image.data, pixels, 0, pixels.Length);
                using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                for (int row = 0; row < height; row++) Marshal.Copy(pixels, row * image.bytes_per_line, bitmap.GetPixels() + row * bitmap.RowBytes, width * 4);
                return Encode(bitmap, maximum);
            }
            finally { XDestroyImage(pointer); }
        }
    }

    internal static CompanionWindowImage Encode(SKBitmap bitmap, int maximum)
    {
        double scale = Math.Min(1d, maximum / (double)Math.Max(bitmap.Width, bitmap.Height));
        using var small = scale < 1 ? bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale))), new SKSamplingOptions(SKCubicResampler.Mitchell)) : null;
        using var image = SKImage.FromBitmap(small ?? bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new("data:image/png;base64," + Convert.ToBase64String(png.ToArray()), image.Width, image.Height);
    }

    [StructLayout(LayoutKind.Sequential)] struct XWindowAttributes { public int x, y, width, height, border, depth; public IntPtr visual; public ulong root; public int c_class, bit_gravity, win_gravity, backing_store; public ulong backing_planes, backing_pixel; public int save_under; public ulong colormap; public int map_installed, map_state; public long all_event_masks, your_event_mask, do_not_propagate_mask; public int override_redirect; public IntPtr screen; }
    [StructLayout(LayoutKind.Sequential)] struct XImage { public int width, height, xoffset, format; public IntPtr data; public int byte_order, bitmap_unit, bitmap_bit_order, bitmap_pad, depth, bytes_per_line, bits_per_pixel; public ulong red_mask, green_mask, blue_mask; public IntPtr obdata; }
    [DllImport("libX11.so.6")] static extern int XGetWindowAttributes(IntPtr display, ulong window, out XWindowAttributes attributes);
    [DllImport("libX11.so.6")] static extern int XTranslateCoordinates(IntPtr display, ulong source, ulong destination, int sourceX, int sourceY, out int destX, out int destY, out ulong child);
    [DllImport("libX11.so.6")] static extern int XQueryTree(IntPtr display, ulong window, out ulong root, out ulong parent, out IntPtr children, out uint count);
    [DllImport("libX11.so.6")] static extern IntPtr XGetImage(IntPtr display, ulong drawable, int x, int y, uint width, uint height, ulong planeMask, int format);
    [DllImport("libX11.so.6")] static extern int XDestroyImage(IntPtr image);
    [DllImport("libX11.so.6")] static extern int XFreePixmap(IntPtr display, ulong pixmap);
    [DllImport("libX11.so.6")] static extern ulong XGetSelectionOwner(IntPtr display, ulong selection);
    [DllImport("libX11.so.6")] static extern int XSync(IntPtr display, bool discard);
    [DllImport("libXcomposite.so.1")] static extern ulong XCompositeNameWindowPixmap(IntPtr display, ulong window);
    [DllImport("libXcomposite.so.1")] static extern void XCompositeRedirectWindow(IntPtr display, ulong window, int update);
    [DllImport("libXcomposite.so.1")] static extern void XCompositeUnredirectWindow(IntPtr display, ulong window, int update);
}

internal static class LinuxCapture
{
    internal static async Task<CompanionWindowImage> CaptureAsync(IntPtr window, int maximum, bool fullScreen, CancellationToken token)
        => await Task.Run(() => { token.ThrowIfCancellationRequested(); return CompanionNative.Capture(window, maximum, fullScreen); }, token);
}
