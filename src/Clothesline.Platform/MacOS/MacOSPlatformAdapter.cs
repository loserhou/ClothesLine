using System.Diagnostics;
using Clothesline.Platform.Abstractions;
using static Clothesline.Platform.MacOS.MacNative;

namespace Clothesline.Platform.MacOS;

/// <summary>
/// macOS 平台适配层：CoreGraphics 全局光标/鼠标状态、NSWindow 鼠标穿透与跨桌面常驻、NSPasteboard 截屏捕获、访达定位
/// </summary>
public class MacOSPlatformAdapter : IPlatformAdapter
{
    // NSWindowCollectionBehavior: CanJoinAllSpaces | Stationary | IgnoresCycle
    // 晾衣绳出现在每个桌面（Space）上，不参与 Mission Control 动画与 Cmd+` 窗口切换；
    // 未设置 FullScreenAuxiliary，因此不会覆盖在全屏应用之上（即天然的全屏避让）。
    private const ulong OverlayCollectionBehavior = (1UL << 0) | (1UL << 4) | (1UL << 6);

    public (int X, int Y) GetCursorPosition()
    {
        // CoreGraphics 全局坐标：主屏左上角为原点、单位 point，与 Avalonia macOS 的屏幕/窗口坐标一致
        var evt = CGEventCreate(IntPtr.Zero);
        if (evt == IntPtr.Zero) return (0, 0);
        try
        {
            var p = CGEventGetLocation(evt);
            return ((int)Math.Round(p.X), (int)Math.Round(p.Y));
        }
        finally
        {
            CFRelease(evt);
        }
    }

    public void PrepareOverlay(IntPtr windowHandle)
    {
        var window = ResolveWindow(windowHandle);
        if (window == IntPtr.Zero) return;
        SendVoid(window, Sel("setCollectionBehavior:"), OverlayCollectionBehavior);
        SendVoid(window, Sel("setHasShadow:"), false);
    }

    public void SetWindowPassMouse(IntPtr windowHandle, bool passMouse)
    {
        var window = ResolveWindow(windowHandle);
        if (window == IntPtr.Zero) return;
        SendVoid(window, Sel("setIgnoresMouseEvents:"), passMouse);
    }

    public bool IsOverCaptionButtons((int X, int Y) cursor, IntPtr overlayWindow)
    {
        // macOS 的窗口红绿灯按钮跟随各窗口而非固定在屏幕角落；晾衣绳挂在菜单栏下方，无需全局避让区
        return false;
    }

    public bool IsLeftMouseButtonDown()
    {
        return CGEventSourceButtonState(kCGEventSourceStateCombinedSessionState, kCGMouseButtonLeft);
    }

    public bool TrySaveClipboardImage(string targetPngPath)
    {
        return MacPasteboard.TrySaveImageAsPng(targetPngPath);
    }

    public IReadOnlyList<string> GetClipboardFilePaths()
    {
        return MacPasteboard.GetFilePaths();
    }

    public void OpenFile(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                Arguments = $"\"{filePath}\"",
                UseShellExecute = false
            });
        }
        catch
        {
            // 忽略打开异常
        }
    }

    public void LocateInFileManager(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                Arguments = $"-R \"{filePath}\"",
                UseShellExecute = false
            });
        }
        catch
        {
            // 忽略访达启动异常
        }
    }

    public bool IsFullScreenAppActive()
    {
        // 全屏应用运行在独立 Space 中，晾衣绳窗口不会出现在那里（见 OverlayCollectionBehavior），无需额外判定
        return false;
    }

    public void CopyFilesToClipboard(IReadOnlyList<string> filePaths)
    {
        var validFiles = filePaths.Where(File.Exists).ToList();
        if (validFiles.Count == 0) return;

        try
        {
            // 利用 macOS 内置 osascript 将 POSIX 路径列表压入剪贴板
            var script = string.Join(", ", validFiles.Select(p => $"POSIX file \"{p.Replace("\"", "\\\"")}\""));
            var appleScript = $"set the clipboard to {{{script}}}";

            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                ArgumentList = { "-e", appleScript },
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(1000);
        }
        catch
        {
            // 忽略剪贴板写入异常
        }
    }
}
