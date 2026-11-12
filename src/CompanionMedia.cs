using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace BigBlueFish;

// The public x64 GStreamer 1.x C ABI. No Python runtime or external player is used.
internal static class CompanionMedia
{
    const string Core = "bbf-gst", App = "bbf-gst-app", Glib = "bbf-glib";
    static readonly object gate = new();
    static bool initialized;
    internal const ulong Second = 1_000_000_000;
    internal static bool HardwareAvailable { get; private set; }
    internal static string HardwareFactory => "vavp9alphadecodebin";
    // Bundled runtimes provide the combined element; system GStreamer (for example 1.20 on Ubuntu 22.04) only has the separate ones.
    internal static string SoftwareTransform
    {
        get
        {
            var factory = gst_element_factory_find("videoconvertscale");
            if (factory == IntPtr.Zero) return "videoconvert ! videoscale";
            gst_object_unref(factory);
            return "videoconvertscale";
        }
    }

    static CompanionMedia() => NativeLibrary.SetDllImportResolver(typeof(CompanionMedia).Assembly, Resolve);
    static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        var file = name switch
        {
            Core => "libgstreamer-1.0.so.0",
            App => "libgstapp-1.0.so.0",
            Glib => "libglib-2.0.so.0",
            _ => null
        };
        if (file == null) return IntPtr.Zero;
        return NativeLibrary.Load(file);
    }
    internal static void Initialize()
    {
        lock (gate)
        {
            if (initialized) return;
            if (IntPtr.Size != 8) throw new PlatformNotSupportedException("动画组件需要64位进程。");
            gst_init(IntPtr.Zero, IntPtr.Zero);
            var factory = gst_element_factory_find(HardwareFactory);
            HardwareAvailable = factory != IntPtr.Zero;
            if (factory != IntPtr.Zero) gst_object_unref(factory);
            initialized = true;

        }
    }
    internal static string Error(IntPtr error)
    {
        if (error == IntPtr.Zero) return "未知动画错误";
        try { return Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8)) ?? "未知动画错误"; }
        finally { g_error_free(error); }
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MapInfo
    {
        internal IntPtr Memory; internal int Flags; internal IntPtr Data;
        internal nuint Size, MaxSize;
        internal IntPtr User0, User1, User2, User3, Reserved0, Reserved1, Reserved2, Reserved3;
    }
    // GstMiniObject is 64 bytes on Linux x64; GstBuffer.pts follows pool.
    internal static double Timestamp(IntPtr buffer) => unchecked((ulong)Marshal.ReadInt64(buffer, 72)) is var ns && ns != ulong.MaxValue ? ns / (double)Second : 0;
    internal static string Quote(string value) => "\"" + value.Replace("\\", "/").Replace("\"", "\\\"") + "\"";
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] static extern void gst_init(IntPtr argc, IntPtr argv);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr gst_element_factory_find([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gst_parse_launch([MarshalAs(UnmanagedType.LPUTF8Str)] string pipeline, out IntPtr error);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gst_bin_get_by_name(IntPtr bin, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern int gst_element_set_state(IntPtr element, int state);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern int gst_element_get_state(IntPtr element, out int state, out int pending, ulong timeout);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern int gst_element_seek(IntPtr element, double rate, int format, int flags, int startType, long start, int stopType, long stop);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gst_element_get_bus(IntPtr element);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gst_bus_pop_filtered(IntPtr bus, uint types);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern void gst_message_parse_error(IntPtr message, out IntPtr error, out IntPtr debug);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern void gst_object_unref(IntPtr obj);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern void gst_mini_object_unref(IntPtr obj);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gst_sample_get_buffer(IntPtr sample);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern int gst_buffer_map(IntPtr buffer, out MapInfo info, int flags);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern void gst_buffer_unmap(IntPtr buffer, ref MapInfo info);
    [DllImport(App, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gst_app_sink_try_pull_sample(IntPtr sink, ulong timeout);
    [DllImport(App, CallingConvention = CallingConvention.Cdecl)] internal static extern int gst_app_sink_is_eos(IntPtr sink);
    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] static extern void g_error_free(IntPtr error);
    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] internal static extern void g_free(IntPtr memory);
}

internal sealed class CompanionMediaStream : IDisposable
{
    readonly object sync = new();
    IntPtr pipeline, sink, bus;
    bool disposed;
    bool ended;
    internal int Width { get; }
    internal int Height { get; }
    internal bool Hardware { get; }
    internal string Description => Hardware ? "GPU VP9 + alpha" : $"CPU VP9 + alpha ({Width}×{Height})";
    internal CompanionMediaStream(string file, bool hardware, int width, int height, double start, double? end, double rate)
    {
        CompanionMedia.Initialize(); Hardware = hardware; Width = width; Height = height;
        var decoder = hardware ? CompanionMedia.HardwareFactory : "vp9alphadecodebin";
        var transform = hardware ? "vapostproc" : CompanionMedia.SoftwareTransform;
        var prefix = $"filesrc location={CompanionMedia.Quote(file)} ! matroskademux ! vp9parse ! {decoder} ! ";
        var output = $"video/x-raw,format=BGRA,width={width},height={height} ! appsink name=frames sync=true max-buffers=1 drop=true wait-on-eos=false max-lateness=40000000 qos=true";
        var text = prefix + transform + " ! " + output;
        pipeline = CompanionMedia.gst_parse_launch(text, out var error);
        try
        {
            if (error != IntPtr.Zero) throw new IOException(CompanionMedia.Error(error));
            if (pipeline == IntPtr.Zero) throw new IOException("动画管线未创建。");
            sink = CompanionMedia.gst_bin_get_by_name(pipeline, "frames"); bus = CompanionMedia.gst_element_get_bus(pipeline);
            if (CompanionMedia.gst_element_set_state(pipeline, 3) == 0) throw new IOException("动画预加载失败。");
            if (CompanionMedia.gst_element_get_state(pipeline, out _, out _, 8 * CompanionMedia.Second) == 0) throw new IOException(ReadError() ?? "动画解码未能就绪。");
            if (start > 0 || end.HasValue || rate != 1)
                if (CompanionMedia.gst_element_seek(sink, rate, 3, 3, 1, (long)(start * CompanionMedia.Second), end.HasValue ? 1 : 0, end.HasValue ? (long)(end.Value * CompanionMedia.Second) : 0) == 0)
                    throw new IOException("动画片段定位失败。");
        }
        catch { Dispose(); throw; }
    }
    internal void SetPaused(bool paused) { lock (sync) { if (!disposed) CompanionMedia.gst_element_set_state(pipeline, paused ? 3 : 4); } }
    internal CompanionMediaFrame? Read()
    {
        lock (sync)
        {
            if (disposed) return null;
            var sample = CompanionMedia.gst_app_sink_try_pull_sample(sink, 200_000_000);
            if (sample == IntPtr.Zero) { var error = ReadError(); if (error != null) throw new IOException(error); return null; }
            return new(sample, Width, Height);
        }
    }
    internal bool Ended { get { lock (sync) return disposed || (ended || CompanionMedia.gst_app_sink_is_eos(sink) != 0); } }
    string? ReadError()
    {
        if (bus == IntPtr.Zero) return null;
        var m = CompanionMedia.gst_bus_pop_filtered(bus, 3); if (m == IntPtr.Zero) return null;
        try { if (Marshal.ReadInt32(m, 64) == 1) { ended = true; return null; } CompanionMedia.gst_message_parse_error(m, out var error, out var debug); if (debug != IntPtr.Zero) CompanionMedia.g_free(debug); return CompanionMedia.Error(error); }
        finally { CompanionMedia.gst_mini_object_unref(m); }
    }
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return; disposed = true;
            if (pipeline != IntPtr.Zero) CompanionMedia.gst_element_set_state(pipeline, 1);
            if (sink != IntPtr.Zero) CompanionMedia.gst_object_unref(sink);
            if (bus != IntPtr.Zero) CompanionMedia.gst_object_unref(bus);
            if (pipeline != IntPtr.Zero) CompanionMedia.gst_object_unref(pipeline);
            pipeline = sink = bus = IntPtr.Zero;
        }
    }
}
internal sealed class CompanionMediaFrame : IDisposable
{
    IntPtr sample, buffer; CompanionMedia.MapInfo map;
    internal IntPtr Data => map.Data;
    internal int Stride { get; }
    internal int Width { get; }
    internal int Height { get; }
    internal double Seconds { get; }
    internal CompanionMediaFrame(IntPtr sample, int width, int height)
    {
        this.sample = sample; Width = width; Height = height; buffer = CompanionMedia.gst_sample_get_buffer(sample);
        try
        {
            Seconds = CompanionMedia.Timestamp(buffer);
            if (CompanionMedia.gst_buffer_map(buffer, out map, 1) == 0) throw new IOException("动画帧映射失败。");
            Stride = checked((int)(map.Size / (nuint)height));
            if (Stride < width * 4 || map.Size < (nuint)(Stride * height)) throw new IOException("动画帧尺寸不完整。");
        }
        catch { Dispose(); throw; }
    }
    public void Dispose() { if (sample == IntPtr.Zero) return; if (map.Data != IntPtr.Zero) CompanionMedia.gst_buffer_unmap(buffer, ref map); CompanionMedia.gst_mini_object_unref(sample); sample = IntPtr.Zero; }
}
