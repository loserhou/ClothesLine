namespace Clothesline.Platform.Abstractions;

/// <summary>
/// 跨平台底层系统能力统一适配接口（Windows & macOS）
/// </summary>
public interface IPlatformAdapter
{
    /// <summary>
    /// 将文件列表写入系统原生剪贴板（供聊天软件、资源管理器直接粘贴）
    /// </summary>
    void CopyFilesToClipboard(IReadOnlyList<string> filePaths);

    /// <summary>
    /// 调用系统默认关联应用直接打开文件
    /// </summary>
    void OpenFile(string filePath);

    /// <summary>
    /// 在系统文件管理器（Windows 资源管理器 / macOS 访达）中定位并高亮选中该文件
    /// </summary>
    void LocateInFileManager(string filePath);

    /// <summary>
    /// 检测当前系统是否有前台全屏应用（如全屏游戏、全屏播放视频）在运行
    /// </summary>
    bool IsFullScreenAppActive();

    /// <summary>
    /// 获取当前全局鼠标光标的绝对屏幕坐标 (X, Y)
    /// </summary>
    (int X, int Y) GetCursorPosition();

    /// <summary>
    /// 初始化悬浮叠加层窗口属性（设置 WS_EX_NOACTIVATE、WS_EX_LAYERED、WS_EX_TOOLWINDOW 等防抢焦点与分层特性）
    /// </summary>
    void PrepareOverlay(IntPtr windowHandle);

    /// <summary>
    /// 设置窗口鼠标穿透状态（当晾衣绳收起或鼠标不在卡片上时启用穿透，光标移入卡片时禁用穿透）
    /// </summary>
    void SetWindowPassMouse(IntPtr windowHandle, bool passMouse);

    /// <summary>
    /// 检测鼠标光标当前是否位于最大化窗口的标题栏按钮（关闭/最小化）区域上，用于防误触
    /// </summary>
    bool IsOverCaptionButtons((int X, int Y) cursor, IntPtr overlayWindow);

    /// <summary>
    /// 检测当前鼠标左键是否处于按下状态（用于全局拖拽感知与自动迎客唤醒）
    /// </summary>
    bool IsLeftMouseButtonDown();

    /// <summary>
    /// 从系统原生剪贴板读取位图数据并安全保存为标准 PNG 文件
    /// </summary>
    bool TrySaveClipboardImage(string targetPngPath);

    /// <summary>
    /// 读取系统剪贴板中「复制的文件」路径列表（资源管理器 Ctrl+C / 访达 Cmd+C）；没有文件时返回空列表
    /// </summary>
    IReadOnlyList<string> GetClipboardFilePaths();
}
