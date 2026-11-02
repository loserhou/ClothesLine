using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using Clothesline.Platform.Abstractions;
using SkiaSharp;

namespace Clothesline.Platform.Windows;

internal static partial class Win32
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DROPFILES
    {
        public int pFiles;
        public POINT pt;
        public bool fNC;
        public bool fWide;
    }

    public const uint CF_HDROP = 15;
    public const uint CF_BITMAP = 2;
    public const uint CF_DIB = 8;
    public const uint CF_DIBV5 = 17;
    public const uint GMEM_MOVEABLE = 0x0002;
    public const uint GMEM_ZEROINIT = 0x0040;

    [LibraryImport("user32.dll")]
    public static partial uint GetClipboardSequenceNumber();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetDesktopWindow();

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetShellWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenClipboard(IntPtr hWndNewOwner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    public static partial IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr GetClipboardData(uint uFormat);

    [LibraryImport("kernel32.dll")]
    public static partial nuint GlobalSize(IntPtr hMem);

    [LibraryImport("shell32.dll", EntryPoint = "DragQueryFileW")]
    public static unsafe partial uint DragQueryFile(IntPtr hDrop, uint iFile, char* lpszFile, uint cch);

    [LibraryImport("kernel32.dll")]
    public static partial IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll")]
    public static partial IntPtr GlobalLock(IntPtr hMem);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalUnlock(IntPtr hMem);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int nIndex);

    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    public static partial int GetWindowLong(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    public static partial int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetPropW(IntPtr hWnd, string lpString, IntPtr hData);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr hWnd);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int vKey);

    public delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, nuint dwRefData);

    [LibraryImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [LibraryImport("comctl32.dll")]
    public static partial IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
}

public class WindowsPlatformAdapter : IPlatformAdapter
{
    public bool IsLeftMouseButtonDown()
    {
        return (Win32.GetAsyncKeyState(1) & 0x8000) != 0;
    }
    public (int X, int Y) GetCursorPosition()
    {
        if (Win32.GetCursorPos(out var pt))
        {
            return (pt.X, pt.Y);
        }
        return (0, 0);
    }

    public void OpenFile(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
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
                FileName = "explorer.exe",
                Arguments = $"/select,\"{filePath}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // 忽略资源管理器启动异常
        }
    }

    public bool IsFullScreenAppActive()
    {
        var foreground = Win32.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;

        var desktop = Win32.GetDesktopWindow();
        var shell = Win32.GetShellWindow();
        if (foreground == desktop || foreground == shell) return false;

        if (Win32.GetWindowRect(foreground, out var rect))
        {
            var screenWidth = Win32.GetSystemMetrics(Win32.SM_CXSCREEN);
            var screenHeight = Win32.GetSystemMetrics(Win32.SM_CYSCREEN);

            // 前台窗口尺寸覆盖或超出全屏幕，判定为全屏独占应用（如全屏游戏/视频）
            if (rect.Left <= 0 && rect.Top <= 0 && rect.Right >= screenWidth && rect.Bottom >= screenHeight)
            {
                return true;
            }
        }

        return false;
    }

    private static readonly Win32.SubclassProc _captionButtonsSubclassProc = SubclassWndProc;

    private static IntPtr SubclassWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, nuint dwRefData)
    {
        const uint WM_NCHITTEST = 0x0084;
        const int HTTRANSPARENT = -1;

        if (uMsg == WM_NCHITTEST)
        {
            int x = (short)(lParam.ToInt64() & 0xFFFF);
            int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

            // 若光标位于右上角窗口控制区 (距右边缘 260px、距顶端 50px 物理像素以内)，强制原生穿透
            if (Win32.GetWindowRect(hWnd, out var winRect))
            {
                if (x >= winRect.Right - 260 && x <= winRect.Right && y >= winRect.Top && y <= winRect.Top + 50)
                {
                    return new IntPtr(HTTRANSPARENT);
                }
            }
            else
            {
                int screenWidth = Win32.GetSystemMetrics(Win32.SM_CXSCREEN);
                if (x >= screenWidth - 260 && x <= screenWidth && y >= 0 && y <= 50)
                {
                    return new IntPtr(HTTRANSPARENT);
                }
            }
        }

        return Win32.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    public void PrepareOverlay(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return;
        try
        {
            Win32.SetPropW(windowHandle, "NonRudeHWND", new IntPtr(1));
            int exStyle = Win32.GetWindowLong(windowHandle, Win32.GWL_EXSTYLE);
            // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TRANSPARENT
            int newStyle = exStyle | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT;
            Win32.SetWindowLong(windowHandle, Win32.GWL_EXSTYLE, newStyle);

            // 挂载 Win32 消息子类化钩子，确保右上角控制区在操作系统驱动层 100% 同步穿透
            Win32.SetWindowSubclass(windowHandle, _captionButtonsSubclassProc, 1001, 0);
        }
        catch
        {
            // 忽略样式设置异常
        }
    }

    public void SetWindowPassMouse(IntPtr windowHandle, bool passMouse)
    {
        if (windowHandle == IntPtr.Zero) return;
        try
        {
            int before = Win32.GetWindowLong(windowHandle, Win32.GWL_EXSTYLE);
            int after = passMouse ? (before | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_LAYERED) : (before & ~Win32.WS_EX_TRANSPARENT);
            if (after != before)
            {
                Win32.SetWindowLong(windowHandle, Win32.GWL_EXSTYLE, after);
            }
        }
        catch
        {
            // 忽略样式设置异常
        }
    }

    public bool IsOverCaptionButtons((int X, int Y) cursor, IntPtr overlayWindow)
    {
        // 1. 优先通过窗口实际物理坐标进行检测 (完美兼容多显示器与不同屏幕摆放)
        if (overlayWindow != IntPtr.Zero && Win32.GetWindowRect(overlayWindow, out var winRect))
        {
            if (cursor.X >= winRect.Right - 260 && cursor.X <= winRect.Right && cursor.Y >= winRect.Top && cursor.Y <= winRect.Top + 50)
            {
                return true;
            }
        }

        // 2. 回退通过主屏幕宽度进行判定
        int screenWidth = Win32.GetSystemMetrics(Win32.SM_CXSCREEN);
        return cursor.X >= screenWidth - 260 && cursor.X <= screenWidth && cursor.Y >= 0 && cursor.Y <= 50;
    }

    public void CopyFilesToClipboard(IReadOnlyList<string> filePaths)
    {
        var validFiles = filePaths.Where(File.Exists).ToList();
        if (validFiles.Count == 0) return;

        if (!Win32.OpenClipboard(IntPtr.Zero)) return;

        try
        {
            Win32.EmptyClipboard();

            // 构建 Win32 HDROP 内存结构 (DROPFILES + 双重 null 结尾宽字符路径列表)
            var dropFilesSize = Marshal.SizeOf<Win32.DROPFILES>();
            var pathString = string.Join("\0", validFiles) + "\0\0";
            var bytes = System.Text.Encoding.Unicode.GetBytes(pathString);
            var totalBytes = (nuint)(dropFilesSize + bytes.Length);

            var hGlobal = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE | Win32.GMEM_ZEROINIT, totalBytes);
            if (hGlobal == IntPtr.Zero) return;

            var pData = Win32.GlobalLock(hGlobal);
            if (pData != IntPtr.Zero)
            {
                try
                {
                    var dropFiles = new Win32.DROPFILES
                    {
                        pFiles = dropFilesSize,
                        pt = new Win32.POINT { X = 0, Y = 0 },
                        fNC = false,
                        fWide = true
                    };

                    Marshal.StructureToPtr(dropFiles, pData, false);
                    Marshal.Copy(bytes, 0, IntPtr.Add(pData, dropFilesSize), bytes.Length);
                }
                finally
                {
                    Win32.GlobalUnlock(hGlobal);
                }

                Win32.SetClipboardData(Win32.CF_HDROP, hGlobal);
            }
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    private readonly object _clipboardLock = new();
    private string? _lastSavedImageHash;
    private DateTime _lastSavedTime = DateTime.MinValue;

    private static string ComputeDibFingerprint(IntPtr ptr, int size)
    {
        try
        {
            using var sha = SHA256.Create();
            unsafe
            {
                using var ums = new UnmanagedMemoryStream((byte*)ptr.ToPointer(), size);
                byte[] hash = sha.ComputeHash(ums);
                return Convert.ToHexString(hash);
            }
        }
        catch
        {
            return $"{size}_{Marshal.ReadInt32(ptr)}";
        }
    }

    public unsafe IReadOnlyList<string> GetClipboardFilePaths()
    {
        var result = new List<string>();
        if (!Win32.IsClipboardFormatAvailable(Win32.CF_HDROP)) return result;

        // 剪贴板可能被其他进程短暂占用，重试几次
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!Win32.OpenClipboard(IntPtr.Zero))
            {
                Thread.Sleep(30);
                continue;
            }

            try
            {
                IntPtr hDrop = Win32.GetClipboardData(Win32.CF_HDROP);
                if (hDrop == IntPtr.Zero) return result;

                uint count = Win32.DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
                for (uint i = 0; i < count; i++)
                {
                    uint len = Win32.DragQueryFile(hDrop, i, null, 0);
                    if (len == 0) continue;
                    var buffer = new char[len + 1];
                    fixed (char* p = buffer)
                    {
                        uint written = Win32.DragQueryFile(hDrop, i, p, len + 1);
                        if (written > 0) result.Add(new string(p, 0, (int)written));
                    }
                }
                return result;
            }
            finally
            {
                Win32.CloseClipboard();
            }
        }

        return result;
    }

    public bool TrySaveClipboardImage(string targetPngPath)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (Win32.OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    IntPtr hDib = Win32.GetClipboardData(Win32.CF_DIB);
                    if (hDib == IntPtr.Zero)
                    {
                        hDib = Win32.GetClipboardData(Win32.CF_DIBV5);
                    }

                    if (hDib != IntPtr.Zero)
                    {
                        IntPtr ptr = Win32.GlobalLock(hDib);
                        if (ptr != IntPtr.Zero)
                        {
                            try
                            {
                                int dibSize = (int)Win32.GlobalSize(hDib);
                                if (dibSize > 40)
                                {
                                    // 核心去重：对剪贴板 DIB 内存计算唯一内容指纹
                                    string currentHash = ComputeDibFingerprint(ptr, dibSize);
                                    lock (_clipboardLock)
                                    {
                                        if (_lastSavedImageHash == currentHash &&
                                            (DateTime.UtcNow - _lastSavedTime).TotalMilliseconds < 2500)
                                        {
                                            // 2.5秒内完全相同的剪贴板 DIB 位图数据（典型的多格式写入或系统历史记录唤醒触发），拦截去重
                                            return false;
                                        }
                                    }

                                    int headerSize = Marshal.ReadInt32(ptr);
                                    short bitCount = Marshal.ReadInt16(ptr + 14);
                                    int compression = Marshal.ReadInt32(ptr + 16);
                                    int clrUsed = Marshal.ReadInt32(ptr + 32);

                                    int colorTableBytes = 0;
                                    if (bitCount <= 8)
                                    {
                                        int colors = clrUsed > 0 ? clrUsed : (1 << bitCount);
                                        colorTableBytes = colors * 4;
                                    }
                                    else if ((bitCount == 16 || bitCount == 32) && compression == 3)
                                    {
                                        colorTableBytes = 12; // 3 * DWORD
                                    }
                                    else if (clrUsed > 0)
                                    {
                                        colorTableBytes = clrUsed * 4;
                                    }

                                    int offBits = 14 + headerSize + colorTableBytes;
                                    int totalBmpSize = 14 + dibSize;

                                    byte[] bmpData = new byte[totalBmpSize];
                                    bmpData[0] = 0x42; // 'B'
                                    bmpData[1] = 0x4D; // 'M'
                                    BitConverter.GetBytes(totalBmpSize).CopyTo(bmpData, 2);
                                    BitConverter.GetBytes(0).CopyTo(bmpData, 6);
                                    BitConverter.GetBytes(offBits).CopyTo(bmpData, 10);

                                    Marshal.Copy(ptr, bmpData, 14, dibSize);

                                    using var bmpStream = new MemoryStream(bmpData);
                                    using var codec = SKCodec.Create(bmpStream);
                                    if (codec != null)
                                    {
                                        using var skBitmap = SKBitmap.Decode(codec);
                                        if (skBitmap != null && skBitmap.Width > 0 && skBitmap.Height > 0)
                                        {
                                            var dir = Path.GetDirectoryName(targetPngPath);
                                            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                                            using var skImage = SKImage.FromBitmap(skBitmap);
                                            using var pngData = skImage.Encode(SKEncodedImageFormat.Png, 100);
                                            using var fs = File.Create(targetPngPath);
                                            pngData.SaveTo(fs);
                                            fs.Flush();

                                            // 记录保存成功的内容指纹与时间戳
                                            lock (_clipboardLock)
                                            {
                                                _lastSavedImageHash = currentHash;
                                                _lastSavedTime = DateTime.UtcNow;
                                            }

                                            return File.Exists(targetPngPath);
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                Win32.GlobalUnlock(hDib);
                            }
                        }
                    }
                }
                finally
                {
                    Win32.CloseClipboard();
                }
            }
            Thread.Sleep(50);
        }

        return false;
    }
}
