using static Clothesline.Platform.MacOS.MacNative;

namespace Clothesline.Platform.MacOS;

/// <summary>
/// NSPasteboard 通用剪贴板桥接：变更计数轮询 + 位图（PNG/TIFF）提取为 PNG 文件
/// </summary>
internal static class MacPasteboard
{
    private const ulong NSBitmapImageFileTypePNG = 4;

    /// <summary>
    /// 剪贴板变更序号（每次内容变化自增，读取开销极低，适合定时轮询）
    /// </summary>
    public static long ChangeCount()
    {
        var pool = objc_autoreleasePoolPush();
        try
        {
            var pb = Send(Class("NSPasteboard"), Sel("generalPasteboard"));
            return pb == IntPtr.Zero ? 0 : SendLong(pb, Sel("changeCount"));
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    /// <summary>
    /// 若剪贴板当前是一张位图（截屏/复制图片），保存为 PNG。
    /// 访达中复制文件时剪贴板也会附带文件图标 TIFF，此时存在 file-url，直接忽略，避免把图标误当截屏挂上绳。
    /// </summary>
    public static bool TrySaveImageAsPng(string targetPngPath)
    {
        var pool = objc_autoreleasePoolPush();
        try
        {
            var pb = Send(Class("NSPasteboard"), Sel("generalPasteboard"));
            if (pb == IntPtr.Zero) return false;

            var types = Send(pb, Sel("types"));
            if (types == IntPtr.Zero) return false;
            if (Contains(types, "public.file-url")) return false;

            IntPtr png = IntPtr.Zero;
            if (Contains(types, "public.png"))
            {
                png = Send(pb, Sel("dataForType:"), NSString("public.png"));
            }
            else if (Contains(types, "public.tiff"))
            {
                var tiff = Send(pb, Sel("dataForType:"), NSString("public.tiff"));
                if (tiff == IntPtr.Zero) return false;
                var rep = Send(Class("NSBitmapImageRep"), Sel("imageRepWithData:"), tiff);
                if (rep == IntPtr.Zero) return false;
                var props = Send(Class("NSDictionary"), Sel("dictionary"));
                png = Send(rep, Sel("representationUsingType:properties:"), NSBitmapImageFileTypePNG, props);
            }

            if (png == IntPtr.Zero) return false;

            var length = (int)SendULong(png, Sel("length"));
            var bytes = Send(png, Sel("bytes"));
            if (length <= 0 || bytes == IntPtr.Zero) return false;

            var buffer = new byte[length];
            System.Runtime.InteropServices.Marshal.Copy(bytes, buffer, 0, length);
            File.WriteAllBytes(targetPngPath, buffer);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    /// <summary>
    /// 读取访达复制的文件路径（每个 pasteboard item 的 public.file-url）
    /// </summary>
    public static IReadOnlyList<string> GetFilePaths()
    {
        var result = new List<string>();
        var pool = objc_autoreleasePoolPush();
        try
        {
            var pb = Send(Class("NSPasteboard"), Sel("generalPasteboard"));
            if (pb == IntPtr.Zero) return result;

            var items = Send(pb, Sel("pasteboardItems"));
            if (items == IntPtr.Zero) return result;

            var fileUrlType = NSString("public.file-url");
            ulong count = SendULong(items, Sel("count"));
            for (ulong i = 0; i < count; i++)
            {
                var item = SendIndex(items, Sel("objectAtIndex:"), i);
                var urlString = Send(item, Sel("stringForType:"), fileUrlType);
                if (urlString == IntPtr.Zero) continue;

                var url = Send(Class("NSURL"), Sel("URLWithString:"), urlString);
                if (url == IntPtr.Zero) continue;
                // file:///.file/id=... 形式的引用 URL 需先解析为真实路径
                var resolved = Send(url, Sel("filePathURL"));
                var path = Send(resolved != IntPtr.Zero ? resolved : url, Sel("path"));
                if (path == IntPtr.Zero) continue;

                var str = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(Send(path, Sel("UTF8String")));
                if (!string.IsNullOrEmpty(str)) result.Add(str);
            }
        }
        catch
        {
            // 忽略剪贴板读取异常
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
        return result;
    }

    private static bool Contains(IntPtr nsArray, string uti) =>
        SendBool(nsArray, Sel("containsObject:"), NSString(uti));
}
