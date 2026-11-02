using System.Runtime.InteropServices;

namespace Clothesline.Platform.MacOS;

/// <summary>
/// macOS 原生互操作：CoreGraphics 全局光标/鼠标状态 + Objective-C 运行时（NSWindow / NSPasteboard）。
/// 坐标约定：CoreGraphics 全局坐标以主屏左上角为原点、单位为 point，与 Avalonia 在 macOS 上的 Screen.Bounds / Window.Position 一致。
/// </summary>
internal static class MacNative
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    // 确保 AppKit 已加载（NSPasteboard / NSBitmapImageRep 类需要）
    private static readonly IntPtr AppKitHandle =
        NativeLibrary.TryLoad("/System/Library/Frameworks/AppKit.framework/AppKit", out var h) ? h : IntPtr.Zero;

    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint
    {
        public double X;
        public double Y;
    }

    [DllImport(CoreGraphics)] public static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(CoreGraphics)] public static extern CGPoint CGEventGetLocation(IntPtr evt);

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CGEventSourceButtonState(int stateId, uint button);

    [DllImport(CoreFoundation)] public static extern void CFRelease(IntPtr obj);

    public const int kCGEventSourceStateCombinedSessionState = 0;
    public const uint kCGMouseButtonLeft = 0;

    // ===== Objective-C runtime =====
    [DllImport(ObjC)] public static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] public static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] public static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(ObjC)] public static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")] public static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] public static extern IntPtr Send(IntPtr receiver, IntPtr selector, ulong arg, IntPtr arg2);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] public static extern IntPtr SendIndex(IntPtr receiver, IntPtr selector, ulong index);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] public static extern void SendVoid(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] public static extern void SendVoid(IntPtr receiver, IntPtr selector, ulong arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] public static extern long SendLong(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] public static extern ulong SendULong(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] public static extern ulong SendULong(IntPtr receiver, IntPtr selector, ulong arg);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SendBool(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendUtf8(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);

    public static IntPtr Sel(string name) => sel_registerName(name);
    public static IntPtr Class(string name)
    {
        _ = AppKitHandle; // 访问静态字段以确保 AppKit 已加载
        return objc_getClass(name);
    }

    public static IntPtr NSString(string value) =>
        SendUtf8(Class("NSString"), Sel("stringWithUTF8String:"), value);

    /// <summary>
    /// 将 Avalonia 给出的平台句柄解析为 NSWindow（兼容传入 NSView 的情况）
    /// </summary>
    public static IntPtr ResolveWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return IntPtr.Zero;
        var nsWindowClass = Class("NSWindow");
        if (SendBool(handle, Sel("isKindOfClass:"), nsWindowClass)) return handle;
        if (SendBool(handle, Sel("respondsToSelector:"), Sel("window"))) return Send(handle, Sel("window"));
        return IntPtr.Zero;
    }
}
