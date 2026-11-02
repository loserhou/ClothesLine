using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Clothesline.App.Views;
using Clothesline.Platform.Startup;

namespace Clothesline.App.Services;

/// <summary>
/// 系统托盘常驻与右键控制中心管理服务（支持 Win 状态栏点击、自启、剪贴板监控与收纳篮呼出）
/// </summary>
public class TrayManager : IDisposable
{
    private readonly ShelfWindow _shelfWindow;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _watchClipboardItem;
    private NativeMenuItem? _autoHangItem;
    private NativeMenuItem? _autoStartItem;

    public TrayManager(ShelfWindow shelfWindow)
    {
        _shelfWindow = shelfWindow;
        InitializeTray();
    }

    private void InitializeTray()
    {
        _trayIcon = new TrayIcon
        {
            ToolTipText = "Clothesline · 桌面物理晾衣绳",
            IsVisible = true
        };

        // 加载托盘图标
        try
        {
            var uri = new Uri("avares://Clothesline.App/Assets/clothesline.png");
            if (AssetLoader.Exists(uri))
            {
                using var stream = AssetLoader.Open(uri);
                _trayIcon.Icon = new WindowIcon(stream);
            }
        }
        catch
        {
            // 图标加载失败时忽略，Avalonia 会使用默认应用图标
        }

        // 托盘左键点击：智能切换晾衣绳展开/收起
        _trayIcon.Clicked += (_, _) =>
        {
            _shelfWindow.Toggle();
        };

        // 构建原生托盘右键功能菜单
        var menu = new NativeMenu();

        var toggleItem = new NativeMenuItem { Header = "显示 / 收起晾衣绳" };
        toggleItem.Click += (_, _) => _shelfWindow.Toggle();
        menu.Add(toggleItem);

        var basketItem = new NativeMenuItem { Header = "打开晾衣篮 (暂存与收纳)" };
        basketItem.Click += (_, _) => _shelfWindow.OpenBasketWindow();
        menu.Add(basketItem);

        menu.Add(new NativeMenuItemSeparator());

        _watchClipboardItem = new NativeMenuItem
        {
            Header = "监听剪贴板（截屏 / 复制文件）",
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = _shelfWindow.IsClipboardWatcherEnabled
        };
        _watchClipboardItem.Click += (_, _) =>
        {
            bool next = !_shelfWindow.IsClipboardWatcherEnabled;
            _shelfWindow.IsClipboardWatcherEnabled = next;
            _watchClipboardItem.IsChecked = next;
        };
        menu.Add(_watchClipboardItem);

        _autoHangItem = new NativeMenuItem
        {
            Header = "剪贴板内容自动挂上绳索",
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = _shelfWindow.AutoHangScreenshots
        };
        _autoHangItem.Click += (_, _) =>
        {
            bool next = !_shelfWindow.AutoHangScreenshots;
            _shelfWindow.AutoHangScreenshots = next;
            _autoHangItem.IsChecked = next;
        };
        menu.Add(_autoHangItem);

        _autoStartItem = new NativeMenuItem
        {
            Header = "开机自动启动",
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = AutoStartService.IsAutoStartEnabled()
        };
        _autoStartItem.Click += (_, _) =>
        {
            bool next = !AutoStartService.IsAutoStartEnabled();
            AutoStartService.SetAutoStart(next);
            _autoStartItem.IsChecked = next;
        };
        menu.Add(_autoStartItem);

        menu.Add(new NativeMenuItemSeparator());

        var clearItem = new NativeMenuItem { Header = "清空晾衣绳卡片" };
        clearItem.Click += async (_, _) =>
        {
            await _shelfWindow.ClearAllCardsAsync();
        };
        menu.Add(clearItem);

        var exitItem = new NativeMenuItem { Header = "退出 Clothesline" };
        exitItem.Click += (_, _) =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        };
        menu.Add(exitItem);

        _trayIcon.Menu = menu;

        // 注册到应用全局托盘集合中
        var currentIcons = TrayIcon.GetIcons(Application.Current!);
        if (currentIcons == null)
        {
            currentIcons = new TrayIcons();
            TrayIcon.SetIcons(Application.Current!, currentIcons);
        }
        currentIcons.Add(_trayIcon);
    }

    public void Dispose()
    {
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            var currentIcons = TrayIcon.GetIcons(Application.Current!);
            currentIcons?.Remove(_trayIcon);
            _trayIcon = null;
        }
    }
}
