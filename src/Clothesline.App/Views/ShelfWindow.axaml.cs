using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Clothesline.App.Controls;
using Clothesline.App.Services;
using Clothesline.Core.Models;
using Clothesline.Core.Physics;
using Clothesline.Core.Storage;
using Clothesline.Platform;
using Clothesline.Platform.Clipboard;

namespace Clothesline.App.Views;

public partial class ShelfWindow : Window
{
    private const double HiddenY = -340.0;
    private const double RevealedY = 0.0;
    private const double CaptionSafeMargin = 175.0; // 屏幕右上角窗口控制三联键 (最小化/还原/关闭) 绝对物理避让净空区 (DIPs)

    private readonly SpringMotion _revealMotion = new(response: 0.42, damping: 0.82, initial: HiddenY);
    private readonly SpringMotion _ropeMotion = new(response: 0.55, damping: 0.78, initial: 18.0);
    private readonly SpringMotion _scrollMotion = new(response: 0.55, damping: 0.78, initial: 0.0);

    private readonly DispatcherTimer _animTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _edgeTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly TranslateTransform _revealTransform = new();
    private double _lastTimeSeconds;

    private DateTime? _edgeSince;
    private DateTime? _awaySince;
    private bool _isRevealed;

    private readonly List<ShotCard> _cards = [];
    private readonly IStateStore _stateStore = new StateStore();
    private readonly HoverPreviewDispatcher _previewDispatcher;
    private readonly ClipboardWatcher _clipboardWatcher = new();
    private BasketWindow? _basketWindow;
    private long _lastHungScreenshotLength;
    private DateTime _lastHungScreenshotTime = DateTime.MinValue;

    public bool IsRevealed => _isRevealed;
    public bool IsClipboardWatcherEnabled
    {
        get => _clipboardWatcher.IsEnabled;
        set => _clipboardWatcher.IsEnabled = value;
    }
    public bool AutoHangScreenshots { get; set; } = true;

    public ShelfWindow()
    {
        InitializeComponent();
        _previewDispatcher = new HoverPreviewDispatcher(this);

        _animTimer.Tick += OnAnimTick;
        _edgeTimer.Tick += OnEdgeTick;

        AddHandler(DragDrop.DragEnterEvent, OnShelfDragEnter, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        AddHandler(DragDrop.DragOverEvent, OnShelfDragOver, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        AddHandler(DragDrop.DragLeaveEvent, OnShelfDragLeave, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        AddHandler(DragDrop.DropEvent, OnShelfDrop, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

        PointerWheelChanged += OnPointerWheelChanged;

        // 动画循环在所有弹簧静止后会自动停摆；任何指针交互都会将其唤醒
        RootContainer.RenderTransform = _revealTransform;
        AddHandler(PointerMovedEvent, OnAnyPointerActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnAnyPointerActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnAnyPointerActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerExited += (_, _) => WakeAnimation();
        Basket.PointerEntered += (_, _) => WakeAnimation();
        Basket.PointerExited += (_, _) => WakeAnimation();
        _clipboardWatcher.ImageCaptured += OnClipboardImageCaptured;
        _clipboardWatcher.FilesCaptured += OnClipboardFilesCaptured;

        // 晾衣篮点击展开暂存与收纳管理面板
        Basket.Clicked += (_, _) => OpenBasketWindow();

        var basketMenu = new ContextMenu();
        var openBasketItem = new MenuItem { Header = "打开收纳衣篮" };
        openBasketItem.Click += (_, _) => OpenBasketWindow();
        var clearBasketItem = new MenuItem { Header = "清空收纳衣篮" };
        clearBasketItem.Click += async (_, _) =>
        {
            await _stateStore.ClearBasketAsync();
            Basket.Bounce();
            await RefreshBasketCountAsync();
        };
        basketMenu.Items.Add(openBasketItem);
        basketMenu.Items.Add(clearBasketItem);
        Basket.ContextMenu = basketMenu;
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        // 初始化 Overlay 属性: WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TRANSPARENT
        PlatformProvider.Current.PrepareOverlay(hwnd);
        PlatformProvider.Current.SetWindowPassMouse(hwnd, true);

        UpdateWindowBounds(moveToOffscreen: true);

        // 异步加载持久化的挂绳文件数据与收纳篮历史计数
        await LoadInitialItemsAsync();
        await RefreshBasketCountAsync();

        WakeAnimation();
        _edgeTimer.Start();
    }

    private void OnAnyPointerActivity(object? sender, PointerEventArgs e) => WakeAnimation();

    /// <summary>
    /// 唤醒 60fps 物理动画循环。循环在全部弹簧静止后自行停止，避免空闲时持续占用 CPU 与渲染。
    /// </summary>
    private void WakeAnimation()
    {
        if (_animTimer.IsEnabled) return;
        _lastTimeSeconds = _stopwatch.Elapsed.TotalSeconds;
        _animTimer.Start();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _animTimer.Stop();
        _edgeTimer.Stop();
        _previewDispatcher.Dispose();
        _clipboardWatcher.Dispose();
        _basketWindow?.Close();
        base.OnClosing(e);
    }

    private void OnClipboardImageCaptured(object? sender, string filePath)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                if ((DateTime.UtcNow - _lastHungScreenshotTime).TotalMilliseconds < 2000 &&
                    fileInfo.Length == _lastHungScreenshotLength &&
                    _cards.Count > 0 &&
                    _cards[0].Item.Kind == ItemKind.Image)
                {
                    // 二次防抖防御：忽略极短时间内的同等大小截屏，杜绝连续挂绳两次
                    try { File.Delete(filePath); } catch { }
                    return;
                }

                _lastHungScreenshotLength = fileInfo.Length;
                _lastHungScreenshotTime = DateTime.UtcNow;

                var item = RopeItem.FromFile(filePath, isTemporaryCopy: true);

                if (AutoHangScreenshots)
                {
                    await _stateStore.AddItemAsync(item);
                    var card = CreateAndAddCard(item, snap: false);
                    _cards.Remove(card);
                    _cards.Insert(0, card);

                    card.DropMotion.Snap(-60.0);
                    card.DropMotion.Target = 0.0;
                    card.SwingMotion.Snap(12.0);
                    card.SwingMotion.Target = 0.0;

                    _ropeMotion.Velocity += 16.0;
                    SlideReveal();
                    LayoutCards(snap: false);
                    card.ShowToast("截屏已挂上");
                }
                else
                {
                    // 若关闭自动上绳，截屏文件直接安全收入右侧收纳篮
                    await _stateStore.AddToBasketAsync(item);
                    Basket.Bounce();
                    await RefreshBasketCountAsync();
                }
            }
        });
    }

    /// <summary>
    /// 用户在资源管理器/访达中复制了文件：自动挂上晾衣绳（与截屏共用「自动挂上绳索」开关）。
    /// 已在绳上的文件静默忽略——单击卡片复制自身时也会触发剪贴板变化，不能重复挂或弹提示。
    /// </summary>
    private void OnClipboardFilesCaptured(object? sender, IReadOnlyList<string> paths)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            var fresh = paths
                .Where(p => File.Exists(p) &&
                            !_cards.Any(c => string.Equals(c.Item.FilePath, p, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (fresh.Count == 0) return;

            if (!AutoHangScreenshots)
            {
                foreach (var path in fresh)
                {
                    await _stateStore.AddToBasketAsync(RopeItem.FromFile(path));
                }
                Basket.Bounce();
                await RefreshBasketCountAsync();
                return;
            }

            // 倒序插入，使复制顺序中的第一个文件最终位于最左侧
            ShotCard? first = null;
            for (int i = fresh.Count - 1; i >= 0; i--)
            {
                var item = RopeItem.FromFile(fresh[i]);
                await _stateStore.AddItemAsync(item);

                var card = CreateAndAddCard(item, snap: false);
                _cards.Remove(card);
                _cards.Insert(0, card);
                card.DropMotion.Snap(-60.0);
                card.DropMotion.Target = 0.0;
                card.SwingMotion.Snap(12.0);
                card.SwingMotion.Target = 0.0;
                first = card;
            }

            _ropeMotion.Velocity += 16.0;
            SlideReveal();
            LayoutCards(snap: false);
            first?.ShowToast(fresh.Count == 1 ? "复制的文件已挂上" : $"已挂上 {fresh.Count} 个文件");
        });
    }

    private async Task LoadInitialItemsAsync()
    {
        var items = await _stateStore.LoadAsync();
        foreach (var item in items)
        {
            if (File.Exists(item.FilePath))
            {
                CreateAndAddCard(item, snap: true);
            }
        }

        LayoutCards(snap: true);
    }

    private ShotCard CreateAndAddCard(RopeItem item, bool snap)
    {
        var card = new ShotCard(item);
        card.UnhookRequested += async (_, unhookItem) => await OnCardUnhookRequestedAsync(card, unhookItem);
        card.PermanentDeleteRequested += async (_, purgeItem) => await OnCardPurgeRequestedAsync(card, purgeItem);
        card.ConvertRequested += (_, convertItem) => OnCardConvertRequested(card, convertItem);
        card.Converted += async (_, newFilePath) => await OnCardConvertedAsync(card, newFilePath);
        card.EditImageRequested += (_, editItem) => OpenImageEditor(card, editItem);
        card.MotionKicked += (_, _) => WakeAnimation();
        card.DragStarted += (_, _) => _previewDispatcher.Dismiss();
        card.PointerEntered += (_, _) => WakeAnimation();
        card.PointerExited += (_, _) => WakeAnimation();
        _previewDispatcher.RegisterCard(card);
        WakeAnimation();

        _cards.Add(card);
        CardsCanvas.Children.Add(card);

        if (!snap)
        {
            card.DropMotion.Snap(-46.0);
            card.DropMotion.Target = 0.0;
            card.SwingMotion.Snap(8.0);
            card.SwingMotion.Target = 0.0;
        }

        return card;
    }

    private async Task OnCardConvertedAsync(ShotCard card, string newFilePath)
    {
        if (!File.Exists(newFilePath)) return;

        var newItem = RopeItem.FromFile(newFilePath, isTemporaryCopy: true);
        await _stateStore.AddItemAsync(newItem);

        var newCard = CreateAndAddCard(newItem, snap: false);
        _cards.Remove(newCard);
        int origIndex = _cards.IndexOf(card);
        int insertIndex = origIndex >= 0 ? origIndex + 1 : 0;
        _cards.Insert(Math.Min(insertIndex, _cards.Count), newCard);

        newCard.DropMotion.Snap(-50.0);
        newCard.DropMotion.Target = 0.0;
        newCard.SwingMotion.Snap(12.0);
        newCard.SwingMotion.Target = 0.0;

        _ropeMotion.Velocity += 14.0;
        LayoutCards(snap: false);
    }

    private void OpenImageEditor(ShotCard card, RopeItem item)
    {
        var editor = new ImageEditorWindow(item.FilePath, async (savedPath, isOverwrite) =>
        {
            if (isOverwrite)
            {
                card.ReloadThumbnail();
                card.ShowToast("已保存修改");
            }
            else
            {
                var newItem = RopeItem.FromFile(savedPath, isTemporaryCopy: true);
                await _stateStore.AddItemAsync(newItem);

                var newCard = CreateAndAddCard(newItem, snap: false);
                _cards.Remove(newCard);
                int idx = _cards.IndexOf(card);
                int insertIdx = idx >= 0 ? idx + 1 : 0;
                _cards.Insert(Math.Min(insertIdx, _cards.Count), newCard);

                newCard.DropMotion.Snap(-50.0);
                newCard.DropMotion.Target = 0.0;
                newCard.SwingMotion.Snap(12.0);
                newCard.SwingMotion.Target = 0.0;

                _ropeMotion.Velocity += 14.0;
                LayoutCards(snap: false);
                newCard.ShowToast("已挂上新批注卡片");
            }
        });
        editor.Show();
    }

    private async Task OnCardUnhookRequestedAsync(ShotCard card, RopeItem item)
    {
        _previewDispatcher.Dismiss();

        // 1. 物理飞跃滑入收纳篮动效 (从绳索向右侧收纳篮腾跃并缩小)
        double basketTargetX = (Canvas.GetLeft(Basket) > 0 ? Canvas.GetLeft(Basket) : Math.Max(0, Width - CaptionSafeMargin - Basket.Width - 12.0)) + 10.0;
        card.XMotion.Target = basketTargetX;
        card.DropMotion.Target = -25.0;
        card.ScaleMotion.Target = 0.25;
        card.SwingMotion.Target = 18.0;

        _ropeMotion.Velocity -= 10.0;
        WakeAnimation();

        // 等待轻快的飞跃动画 (200ms)
        await Task.Delay(200);

        _cards.Remove(card);
        CardsCanvas.Children.Remove(card);

        // 铁律：从 state.json 解挂，绝不删除本地源物理文件！
        // StateStore.RemoveItemAsync 会自动将解挂项安全归档进 basket_history.json
        await _stateStore.RemoveItemAsync(item.Id);

        LayoutCards(snap: false);

        // 触发右侧收纳篮的物理弹跳与计数更新
        Basket.Bounce();
        await RefreshBasketCountAsync();
    }

    private async Task OnCardPurgeRequestedAsync(ShotCard card, RopeItem item)
    {
        _previewDispatcher.Dismiss();

        // 缩减下坠消散动效
        card.ScaleMotion.Target = 0.1;
        card.DropMotion.Target = 60.0;
        _ropeMotion.Velocity -= 8.0;
        WakeAnimation();

        await Task.Delay(180);

        _cards.Remove(card);
        CardsCanvas.Children.Remove(card);

        // 彻底清除：从挂绳移除，不移入衣篮；若为临时截图，彻底删除物理文件
        await _stateStore.PurgeItemAsync(item.Id, deletePhysicalTemporaryFile: true);

        LayoutCards(snap: false);
        await RefreshBasketCountAsync();
    }

    private void OnCardConvertRequested(ShotCard card, RopeItem item)
    {
        var cursor = PlatformProvider.Current.GetCursorPosition();
        var radialWindow = new RadialWindow();
        radialWindow.ShowForConversion(item, new PixelPoint(cursor.X, cursor.Y), onConverted: async newFilePath =>
        {
            if (!File.Exists(newFilePath)) return;

            var newItem = RopeItem.FromFile(newFilePath, isTemporaryCopy: true);
            await _stateStore.AddItemAsync(newItem);

            var newCard = CreateAndAddCard(newItem, snap: false);

            _cards.Remove(newCard);
            int origIndex = _cards.IndexOf(card);
            int insertIndex = origIndex >= 0 ? origIndex + 1 : 0;
            _cards.Insert(Math.Min(insertIndex, _cards.Count), newCard);

            // 抛物线飞跃回挂：从轮盘光标处以初速度向上腾跃并弧线归位
            double startFlightX = cursor.X - Position.X - 74.0;
            double startFlightY = cursor.Y - Position.Y - 18.0;

            newCard.XMotion.Snap(startFlightX);
            newCard.DropMotion.Snap(startFlightY);
            newCard.DropMotion.Velocity = -150.0;
            newCard.DropMotion.Target = 0.0;
            newCard.SwingMotion.Snap(12.0);
            newCard.SwingMotion.Target = 0.0;

            _ropeMotion.Velocity += 16.0;
            LayoutCards(snap: false);
            newCard.ShowToast("转换成功并已挂上");
        });
    }

    private void LayoutCards(bool snap)
    {
        EmptyHintBorder.IsVisible = _cards.Count == 0;

        double pitch = 172.0;
        double totalWidth = _cards.Count * pitch;
        double startX = totalWidth <= Width ? (Width - totalWidth) / 2.0 : 40.0;

        CardsCanvas.Width = Math.Max(Width, totalWidth + 80.0);
        _ropeMotion.Target = 18.0 + Math.Min(8, _cards.Count) * 1.5;
        WakeAnimation();

        for (int i = 0; i < _cards.Count; i++)
        {
            var card = _cards[i];
            double targetX = startX + i * pitch;
            card.XMotion.Target = targetX;
            if (snap)
            {
                card.XMotion.Snap(targetX);
                card.DropMotion.Snap(0.0);
                card.SwingMotion.Snap(0.0);
            }
        }
    }

    /// <summary>
    /// 晾衣绳悬挂的顶端 Y：macOS 挂在菜单栏下方（菜单栏层级高于浮动窗口，会遮住绳子）；
    /// 菜单栏自动隐藏时 WorkingArea.Y 与屏幕顶端重合。Windows 直接贴屏幕顶端。
    /// </summary>
    private static int ShelfTopY(Avalonia.Platform.Screen screen) =>
        OperatingSystem.IsMacOS() ? screen.WorkingArea.Y : screen.Bounds.Y;

    public void SlideReveal()
    {
        var screen = Screens.Primary;
        if (screen != null)
        {
            Position = new PixelPoint(screen.Bounds.X, ShelfTopY(screen));
        }

        _isRevealed = true;
        _revealMotion.Target = RevealedY;
        WakeAnimation();
        _edgeSince = null;
        _awaySince = null;

        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        PlatformProvider.Current.SetWindowPassMouse(hwnd, false);
    }

    public void SlideHide()
    {
        _previewDispatcher.Dismiss();
        _isRevealed = false;
        _revealMotion.Target = HiddenY;
        WakeAnimation();
        _edgeSince = null;
        _awaySince = null;

        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        PlatformProvider.Current.SetWindowPassMouse(hwnd, true);
    }

    public void Toggle()
    {
        if (_isRevealed) SlideHide();
        else SlideReveal();
    }

    private void UpdateWindowBounds(bool moveToOffscreen)
    {
        var screen = Screens.Primary;
        if (screen != null)
        {
            var bounds = screen.Bounds;
            double scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
            double dipWidth = bounds.Width / scale;

            // 收起时移至物理屏幕上方 -450px 处，彻底杜绝任何露边或阻挡底层窗口
            int posX = bounds.X;
            int posY = moveToOffscreen ? bounds.Y - 450 : ShelfTopY(screen);

            Position = new PixelPoint(posX, posY);
            Width = dipWidth;
            RootContainer.Width = dipWidth;
            Rope.Width = Math.Max(0, dipWidth - CaptionSafeMargin + 10.0);
            CardsScrollViewer.Width = Math.Max(0, dipWidth - CaptionSafeMargin - Basket.Width - 16.0);
            CardsCanvas.Width = Math.Max(dipWidth, 1920);

            Canvas.SetLeft(EmptyHintBorder, Math.Max(0, (dipWidth - CaptionSafeMargin - EmptyHintBorder.Bounds.Width) / 2.0));
        }
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        double now = _stopwatch.Elapsed.TotalSeconds;
        double dt = Math.Min(0.05, now - _lastTimeSeconds);
        _lastTimeSeconds = now;

        if (dt <= 0) return;

        _revealMotion.Step(dt);
        _ropeMotion.Step(dt);
        _scrollMotion.Step(dt);

        _revealTransform.Y = _revealMotion.Position;
        Rope.Sag = _ropeMotion.Position;

        var scrollOffset = new Vector(_scrollMotion.Position, 0);
        if (CardsScrollViewer.Offset != scrollOffset)
        {
            CardsScrollViewer.Offset = scrollOffset;
        }

        if (EmptyHintBorder.Bounds.Width > 0 && Width > 0)
        {
            Canvas.SetLeft(EmptyHintBorder, (Width - CaptionSafeMargin - EmptyHintBorder.Bounds.Width) / 2.0);
        }

        bool allSettled = _revealMotion.Settled && _ropeMotion.Settled && _scrollMotion.Settled;

        double ropeWidth = Rope.Bounds.Width > 0 ? Rope.Bounds.Width : Width;
        foreach (var card in _cards)
        {
            double anchorX = card.XMotion.Position + 74.0;
            double ropeY = Rope.GetRopeYAt(anchorX, ropeWidth);
            allSettled &= card.StepPhysics(dt, ropeY);
        }

        // 拟物收纳篮常驻悬挂于绳索右端（避开右上角窗口控制区，随绳索弧度物理起伏）
        double basketX = Math.Max(0, Width - CaptionSafeMargin - Basket.Width - 12.0);
        double basketRopeY = Rope.GetRopeYAt(basketX + Basket.Width / 2.0, ropeWidth);
        Canvas.SetLeft(Basket, basketX);
        Canvas.SetTop(Basket, basketRopeY);
        allSettled &= Basket.StepPhysics(dt);

        // 当收起动作彻底完成且已沉淀在上方时，将窗体移到屏幕物理范围之外 (确保 100% 毫无露边)
        if (!_isRevealed && _revealMotion.Position <= -310.0)
        {
            var screen = Screens.Primary;
            if (screen != null && Position.Y >= screen.Bounds.Y)
            {
                Position = new PixelPoint(screen.Bounds.X, screen.Bounds.Y - 450);
            }
        }

        // 全部静止：停掉动画循环，直到下一次交互/状态变化再唤醒
        if (allSettled)
        {
            _animTimer.Stop();
        }
    }

    private void OnEdgeTick(object? sender, EventArgs e)
    {
        var cursor = PlatformProvider.Current.GetCursorPosition();
        var screen = Screens.Primary;
        if (screen == null) return;

        var bounds = screen.Bounds;
        int shelfTop = ShelfTopY(screen);
        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

        // ★ 核心防线 1：系统控制区避让 (Caption Buttons Safe Zone)
        // 当光标移至右上角最小化/最大化/关闭按钮区域，立即禁止展开，已展开则立刻缩回并开启底层穿透
        if (PlatformProvider.Current.IsOverCaptionButtons(cursor, hwnd))
        {
            _edgeSince = null;
            if (_isRevealed)
            {
                SlideHide();
            }
            PlatformProvider.Current.SetWindowPassMouse(hwnd, true);
            return;
        }

        // ★ 核心防线 2：全屏独占应用避让 (FullScreen)
        if (PlatformProvider.Current.IsFullScreenAppActive())
        {
            _edgeSince = null;
            if (_isRevealed)
            {
                SlideHide();
            }
            return;
        }

        bool isDragging = PlatformProvider.Current.IsLeftMouseButtonDown();
        double scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
        int rightSafeZonePx = (int)(CaptionSafeMargin * scale + 40);

        // ★ 核心交互 1：拖拽感知唤醒 (Drag Sensing to Wake)
        // 用户拖拽文件向屏幕上方靠拢时灵敏滑下迎客 (避开右上角系统控制区)
        bool canDragWake = isDragging && cursor.Y >= bounds.Y && cursor.Y <= shelfTop + 70 &&
                           cursor.X >= bounds.X && cursor.X <= bounds.X + bounds.Width - rightSafeZonePx;

        if (canDragWake && !_isRevealed)
        {
            SlideReveal();
            return;
        }

        // ★ 核心交互 2：纯鼠标停靠唤醒 (Hover Reveal) 限制在屏幕中段
        // 避开左侧 160px 系统托盘/桌面切屏区，避开右侧标题栏控制区，彻底防止甩鼠标误触
        int leftSafeZonePx = 160;
        bool inCentralZone = cursor.X >= bounds.X + leftSafeZonePx &&
                             cursor.X <= bounds.X + bounds.Width - rightSafeZonePx;

        bool touchingTop = inCentralZone && cursor.Y >= bounds.Y && cursor.Y <= bounds.Y + 2;

        if (touchingTop && !_isRevealed)
        {
            _edgeSince ??= DateTime.UtcNow;
            if ((DateTime.UtcNow - _edgeSince.Value).TotalMilliseconds >= 280)
            {
                SlideReveal();
            }
        }
        else if (!isDragging)
        {
            _edgeSince = null;
        }

        // 离开判定 (展开状态下光标离开区域 350ms 自动滑回收起)
        if (_isRevealed)
        {
            bool insideShelf = cursor.X >= bounds.X && cursor.X < bounds.X + bounds.Width - rightSafeZonePx &&
                               cursor.Y >= bounds.Y && cursor.Y < shelfTop + 280;
            // 若用户正在操作收纳篮管理窗口，保持晾衣绳不被自动收回
            if (!insideShelf && (_basketWindow == null || !_basketWindow.IsVisible))
            {
                _awaySince ??= DateTime.UtcNow;
                if ((DateTime.UtcNow - _awaySince.Value).TotalMilliseconds >= 350)
                {
                    SlideHide();
                }
            }
            else
            {
                _awaySince = null;
            }
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        _previewDispatcher.Dismiss();
        double maxScroll = Math.Max(0, CardsCanvas.Width - Width);
        double target = Math.Clamp(_scrollMotion.Target - e.Delta.Y * 120.0, 0, maxScroll);
        _scrollMotion.Target = target;
        WakeAnimation();
        e.Handled = true;
    }

    private void OnShelfDragEnter(object? sender, DragEventArgs e)
    {
        SlideReveal();
        UpdateDragVisuals(e);
        e.DragEffects = DragDropEffects.Copy | DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnShelfDragOver(object? sender, DragEventArgs e)
    {
        UpdateDragVisuals(e);
        e.DragEffects = DragDropEffects.Copy | DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnShelfDragLeave(object? sender, DragEventArgs e)
    {
        Rope.IsHighlighted = false;
        Basket.IsHighlighted = false;
        WakeAnimation();
    }

    private void UpdateDragVisuals(DragEventArgs e)
    {
        WakeAnimation();
        // 以收纳篮控件为基准进行 2D 磁吸范围判断 (外扩 35px 宽，25-40px 高)
        var basketPt = e.GetPosition(Basket);
        double bWidth = Basket.Bounds.Width > 0 ? Basket.Bounds.Width : Basket.Width;
        double bHeight = Basket.Bounds.Height > 0 ? Basket.Bounds.Height : Basket.Height;
        bool nearBasket = basketPt.X >= -35 && basketPt.X <= bWidth + 35 &&
                          basketPt.Y >= -25 && basketPt.Y <= bHeight + 40;

        if (nearBasket)
        {
            Basket.IsHighlighted = true;
            Rope.IsHighlighted = false;
        }
        else
        {
            Basket.IsHighlighted = false;
            // 只要进入晾衣绳有效挂载区域（垂直高度在 260px 以内），晾衣绳即刻变亮
            var shelfPt = e.GetPosition(this);
            Rope.IsHighlighted = shelfPt.Y >= 0 && shelfPt.Y <= 260.0;
        }
    }

    private async void OnShelfDrop(object? sender, DragEventArgs e)
    {
        // async void 事件处理器中的异常会直接终止进程，这里统一兜底
        try
        {
            await HandleShelfDropAsync(e);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[Clothesline] drop failed: {ex}");
        }
    }

    private async Task HandleShelfDropAsync(DragEventArgs e)
    {
        Rope.IsHighlighted = false;
        Basket.IsHighlighted = false;

        var paths = DragDropHelper.ExtractFilePaths(e.Data);
        var cardId = DragDropHelper.TryGetCardId(e.Data);
        if (!string.IsNullOrEmpty(cardId))
        {
            var cardById = _cards.FirstOrDefault(c => c.Item.Id == cardId);
            if (cardById != null && !paths.Contains(cardById.Item.FilePath, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(cardById.Item.FilePath);
            }
        }

        if (paths.Count == 0) return;

        var basketPt = e.GetPosition(Basket);
        double bWidth = Basket.Bounds.Width > 0 ? Basket.Bounds.Width : Basket.Width;
        double bHeight = Basket.Bounds.Height > 0 ? Basket.Bounds.Height : Basket.Height;
        bool dropIntoBasket = basketPt.X >= -35 && basketPt.X <= bWidth + 35 &&
                              basketPt.Y >= -25 && basketPt.Y <= bHeight + 40;

        if (dropIntoBasket)
        {
            await StowIntoBasketAsync(paths, cardId);
            e.Handled = true;
            return;
        }

        // 挂上晾衣绳
        bool hasNew = false;
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;

            var existing = _cards.FirstOrDefault(c => string.Equals(c.Item.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.SwingMotion.Velocity += 14.0;
                existing.DropMotion.Velocity -= 12.0;
                existing.ShowToast("已在绳索上");
                continue;
            }

            var item = RopeItem.FromFile(path);
            await _stateStore.AddItemAsync(item);

            var card = CreateAndAddCard(item, snap: false);
            _cards.Remove(card);
            _cards.Insert(0, card);
            hasNew = true;
            card.ShowToast("已挂上晾衣绳");
        }

        if (hasNew)
        {
            _ropeMotion.Velocity += 12.0;
            LayoutCards(snap: false);
        }

        e.Handled = true;
    }

    /// <summary>
    /// 把文件收进晾衣篮：绳上已挂的卡片会被取下（仅解挂，不删源文件），外部文件直接存入。
    /// 绳上的篮子图标与打开的晾衣篮窗口共用这一入口。
    /// </summary>
    public async Task StowIntoBasketAsync(IReadOnlyList<string> paths, string? cardId)
    {
        _previewDispatcher.Dismiss();

        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;

            var existingCard = _cards.FirstOrDefault(c =>
                (!string.IsNullOrEmpty(cardId) && c.Item.Id == cardId) ||
                string.Equals(c.Item.FilePath, path, StringComparison.OrdinalIgnoreCase));

            if (existingCard != null)
            {
                _cards.Remove(existingCard);
                CardsCanvas.Children.Remove(existingCard);
                // 自动解挂并归档至 basket_history.json
                await _stateStore.RemoveItemAsync(existingCard.Item.Id);
            }
            else
            {
                await _stateStore.AddToBasketAsync(RopeItem.FromFile(path));
            }
        }

        LayoutCards(snap: false);
        Basket.Bounce();
        await RefreshBasketCountAsync();
    }

    /// <summary>
    /// 晾衣篮窗口定位：右边缘对齐绳上的篮子图标、紧挨晾衣绳下方弹出，方便把卡片直接往下拖进去；
    /// 超出工作区时贴边收回。
    /// </summary>
    private PixelPoint ComputeBasketWindowPosition(Window window)
    {
        var screen = Screens.Primary;
        if (screen == null) return new PixelPoint(0, 0);

        double scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var work = screen.WorkingArea;
        int w = (int)(window.Width * scale);
        int h = (int)(window.Height * scale);

        double basketRight = Math.Max(0, Width - CaptionSafeMargin - 12.0); // 与 OnAnimTick 中篮子的落点一致
        int x = screen.Bounds.X + (int)(basketRight * scale) - w;
        int y = ShelfTopY(screen) + (int)(250 * scale); // 卡片最低垂到约 250 处

        x = Math.Clamp(x, work.X + 8, Math.Max(work.X + 8, work.Right - w - 8));
        y = Math.Clamp(y, work.Y + 8, Math.Max(work.Y + 8, work.Bottom - h - 8));
        return new PixelPoint(x, y);
    }

    public void OpenBasketWindow()
    {
        if (_basketWindow == null || !_basketWindow.IsVisible)
        {
            _basketWindow = new BasketWindow(_stateStore, item =>
            {
                RehangFromBasket(item);
            }, async () =>
            {
                await RefreshBasketCountAsync();
            }, StowIntoBasketAsync);
            _basketWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            _basketWindow.Position = ComputeBasketWindowPosition(_basketWindow);
            _basketWindow.Closed += async (_, _) =>
            {
                _basketWindow = null;
                await RefreshBasketCountAsync();
            };
            _basketWindow.Show();
        }
        else
        {
            _basketWindow.Activate();
        }
    }

    public void RehangFromBasket(RopeItem item)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (!File.Exists(item.FilePath)) return;

            await _stateStore.AddItemAsync(item);

            var existing = _cards.FirstOrDefault(c => string.Equals(c.Item.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.SwingMotion.Velocity += 14.0;
                existing.DropMotion.Velocity -= 12.0;
                SlideReveal();
                existing.ShowToast("已在绳索上");
                return;
            }

            var card = CreateAndAddCard(item, snap: false);
            _cards.Remove(card);
            _cards.Insert(0, card);

            card.DropMotion.Snap(-60.0);
            card.DropMotion.Target = 0.0;
            card.SwingMotion.Snap(12.0);
            card.SwingMotion.Target = 0.0;

            _ropeMotion.Velocity += 16.0;
            SlideReveal();
            LayoutCards(snap: false);
            await RefreshBasketCountAsync();
            card.ShowToast("已挂上晾衣绳");
        });
    }

    public async Task ClearAllCardsAsync()
    {
        _previewDispatcher.Dismiss();
        foreach (var card in _cards.ToList())
        {
            CardsCanvas.Children.Remove(card);
            await _stateStore.RemoveItemAsync(card.Item.Id);
        }
        _cards.Clear();
        LayoutCards(snap: false);
        Basket.Bounce();
        await RefreshBasketCountAsync();
    }

    public async Task RefreshBasketCountAsync()
    {
        var basketItems = await _stateStore.LoadBasketAsync();
        Basket.ItemCount = basketItems.Count;
        WakeAnimation();
    }
}
