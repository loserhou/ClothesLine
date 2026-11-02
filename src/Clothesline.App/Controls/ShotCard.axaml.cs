using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Clothesline.Core.Models;
using Clothesline.Core.Physics;
using Clothesline.Engines;
using Clothesline.Platform;

namespace Clothesline.App.Controls;

public partial class ShotCard : UserControl
{
    public RopeItem Item { get; }

    public event EventHandler<RopeItem>? UnhookRequested;
    public event EventHandler<RopeItem>? PermanentDeleteRequested;
    public event EventHandler<RopeItem>? ConvertRequested;
    public event EventHandler<string>? Converted;
    public event EventHandler<RopeItem>? EditImageRequested;

    /// <summary>卡片自身给弹簧注入了速度（如单击复制时的晃动），宿主需唤醒动画循环</summary>
    public event EventHandler? MotionKicked;

    /// <summary>卡片开始被拖出（宿主应收起悬停预览，避免预览浮窗挡住拖放目标）</summary>
    public event EventHandler? DragStarted;

    private const int ThumbnailDecodeWidth = 280;

    // 每帧复用的变换对象，避免 60fps 下持续分配 TransformGroup/ScaleTransform/RotateTransform
    private readonly ScaleTransform _scaleTransform = new(1.0, 1.0);
    private readonly RotateTransform _rotateTransform = new(0, 74, 0);

    public readonly SpringMotion XMotion = new(response: 0.55, damping: 0.78, initial: 0.0);
    public readonly SpringMotion DropMotion = new(response: 0.42, damping: 0.72, initial: -46.0);
    public readonly SpringMotion SwingMotion = new(response: 2 * Math.PI / Math.Sqrt(46), damping: 2.6 / (2 * Math.Sqrt(46)), initial: 6.0);
    public readonly SpringMotion ScaleMotion = new(response: 0.3, damping: 0.6, initial: 1.0);

    private Point _pointerDownPoint;
    private bool _isPressed;
    private bool _dragInitiated;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(1.2) };

    public ShotCard() : this(new RopeItem { FilePath = "", FileName = "示例文件" })
    {
    }

    public ShotCard(RopeItem item)
    {
        InitializeComponent();
        Item = item;

        DropMotion.Target = 0.0;
        SwingMotion.Target = 0.0;
        ScaleMotion.Target = 1.0;

        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            CopiedToast.IsVisible = false;
        };

        UnhookButton.Click += (_, e) =>
        {
            e.Handled = true;
            OnUnhookClicked();
        };

        UnhookButton.PointerPressed += (_, e) =>
        {
            e.Handled = true;
        };

        PointerEntered += (_, _) =>
        {
            UnhookButton.Opacity = 1.0;
            UnhookButton.IsHitTestVisible = true;
            ScaleMotion.Target = 1.03;
        };

        PointerExited += (_, _) =>
        {
            UnhookButton.Opacity = 0.0;
            UnhookButton.IsHitTestVisible = false;
            ScaleMotion.Target = 1.0;
        };

        RenderTransform = new TransformGroup { Children = { _scaleTransform, _rotateTransform } };

        SetupCardContent();
        SetupContextMenu();
    }

    private void SetupCardContent()
    {
        FileNameText.Text = Item.FileName;
        FileSizeText.Text = Item.FormattedFileSize;

        // 设置右下角常驻格式胶囊徽章
        var badge = Item.Badge;
        BadgeBorder.Background = new SolidColorBrush(Color.Parse(badge.BackgroundColorHex));
        BadgeText.Text = badge.Text;
        BadgeText.Foreground = new SolidColorBrush(Color.Parse(badge.ForegroundColorHex));

        // 先以通用文件吊牌呈现；图片缩略图在后台线程解码完成后再切换，避免批量加载时阻塞 UI 线程
        ImageFrame.IsVisible = false;
        DocFrame.IsVisible = true;
        DocIconSymbol.Text = Item.Kind switch
        {
            ItemKind.Document => "DOC",
            ItemKind.Presentation => "PPT",
            ItemKind.Spreadsheet => "XLS",
            ItemKind.Media => "MEDIA",
            ItemKind.Text => "TXT",
            ItemKind.Archive => "ZIP",
            _ => "FILE"
        };

        if (Item.Kind == ItemKind.Image)
        {
            _ = LoadThumbnailAsync();
        }
    }

    private async Task LoadThumbnailAsync()
    {
        var path = Item.FilePath;
        Bitmap? bitmap;
        try
        {
            bitmap = await Task.Run(() =>
            {
                if (!File.Exists(path)) return null;
                using var stream = File.OpenRead(path);
                // 按需解码到卡片尺寸，避免整图解码占用过多内存
                return Bitmap.DecodeToWidth(stream, ThumbnailDecodeWidth);
            });
        }
        catch
        {
            // 图片解码失败则保持通用文件吊牌
            return;
        }

        if (bitmap == null) return;

        var previous = ThumbnailImage.Source as Bitmap;
        ThumbnailImage.Source = bitmap;
        ImageFrame.IsVisible = true;
        DocFrame.IsVisible = false;
        if (!ReferenceEquals(previous, bitmap)) previous?.Dispose();
    }

    private void SetupContextMenu()
    {
        var menu = new ContextMenu();

        // 1. 格式转换子菜单 (极简右键直出，一键触发卡片内圆环进度条)
        var targetFormats = ConversionService.Current.GetTargetFormats(Item.Extension);
        if (targetFormats.Count > 0)
        {
            var convertMenu = new MenuItem { Header = "格式转换" };
            foreach (var fmt in targetFormats)
            {
                var fmtItem = new MenuItem { Header = $"转为 {fmt}" };
                fmtItem.Click += async (_, _) => await TriggerConversionAsync(fmt);
                convertMenu.Items.Add(fmtItem);
            }
            menu.Items.Add(convertMenu);
        }

        // 2. 图片编辑与批注标注
        if (Item.Kind == ItemKind.Image)
        {
            var editItem = new MenuItem { Header = "编辑与标注图片..." };
            editItem.Click += (_, _) => EditImageRequested?.Invoke(this, Item);
            menu.Items.Add(editItem);
        }

        menu.Items.Add(new Separator());

        var locateItem = new MenuItem { Header = "在文件夹中定位" };
        locateItem.Click += (_, _) => PlatformProvider.Current.LocateInFileManager(Item.FilePath);

        var openItem = new MenuItem { Header = "打开文件" };
        openItem.Click += (_, _) => PlatformProvider.Current.OpenFile(Item.FilePath);

        var copyItem = new MenuItem { Header = "复制文件" };
        copyItem.Click += (_, _) => CopyFileToClipboard();

        var copyPathItem = new MenuItem { Header = "复制文件路径" };
        copyPathItem.Click += async (_, _) => await CopyPathToClipboardAsync();

        var saveCopyItem = new MenuItem { Header = "保存副本 (另存为)..." };
        saveCopyItem.Click += async (_, _) => await SaveCopyAsAsync();

        menu.Items.Add(locateItem);
        menu.Items.Add(openItem);
        menu.Items.Add(copyItem);
        menu.Items.Add(copyPathItem);
        menu.Items.Add(saveCopyItem);

        menu.Items.Add(new Separator());

        var unhookItem = new MenuItem { Header = "从晾衣绳取下 (移入衣篮)" };
        unhookItem.Click += (_, _) => OnUnhookClicked();
        menu.Items.Add(unhookItem);

        var purgeItem = new MenuItem { Header = "彻底删除 (不存入衣篮)" };
        purgeItem.Click += (_, _) => PermanentDeleteRequested?.Invoke(this, Item);
        menu.Items.Add(purgeItem);

        ContextMenu = menu;
    }

    /// <summary>
    /// 原地触发格式转换并驱动卡片上的圆环进度条
    /// </summary>
    public async Task TriggerConversionAsync(string targetFormat)
    {
        ConvertingOverlay.IsVisible = true;
        ConvertProgressRing.Progress = 0.05;
        ConvertPercentText.Text = "准备转换...";

        var progress = new Progress<double>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                ConvertProgressRing.Progress = p;
                ConvertPercentText.Text = $"{(int)(p * 100)}%";
            });
        });

        try
        {
            var newFilePath = await ConversionService.Current.ConvertAsync(Item.FilePath, targetFormat, progress);
            ConvertingOverlay.IsVisible = false;
            Converted?.Invoke(this, newFilePath);
            ShowToast("转换成功并已挂上");
        }
        catch
        {
            ConvertingOverlay.IsVisible = false;
            ShowToast("转换失败");
        }
    }

    public void ReloadThumbnail()
    {
        if (Item.Kind == ItemKind.Image)
        {
            _ = LoadThumbnailAsync();
        }
    }

    public async Task SaveCopyAsAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存文件副本到...",
            DefaultExtension = Item.Extension.ToLowerInvariant(),
            SuggestedFileName = Item.FileName
        });

        if (file != null)
        {
            var destination = file.Path.LocalPath;
            if (File.Exists(Item.FilePath))
            {
                File.Copy(Item.FilePath, destination, overwrite: true);
                // 另存为成功后将卡片路径更新为持久化副本
                Item.FilePath = destination;
                Item.FileName = Path.GetFileName(destination);
                Item.IsTemporaryCopy = false;
                FileNameText.Text = Item.FileName;
                ShowToast("副本已保存");
            }
        }
    }

    private async Task CopyPathToClipboardAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;
        await clipboard.SetTextAsync(Item.FilePath);
        ShowToast("路径已复制");
    }

    private void CopyFileToClipboard()
    {
        PlatformProvider.Current.CopyFilesToClipboard([Item.FilePath]);
        ShowToast("已复制到剪贴板");
        SwingMotion.Velocity += 4.0;
        MotionKicked?.Invoke(this, EventArgs.Empty);
    }

    public void ShowToast(string message)
    {
        if (CopiedToast.Child is TextBlock tb)
        {
            tb.Text = message;
        }
        CopiedToast.IsVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void OnUnhookClicked()
    {
        TopClip.IsOpened = true;
        UnhookRequested?.Invoke(this, Item);
    }

    protected override async void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_isPressed || _dragInitiated) return;

        var current = e.GetPosition(this);
        var diff = current - _pointerDownPoint;

        // 位移判定阈值 5px
        if (Math.Sqrt(diff.X * diff.X + diff.Y * diff.Y) >= 5.0)
        {
            _dragInitiated = true;

            // 检测修饰键：Windows 检测 Ctrl，macOS 检测 Alt/Option
            bool isModifierPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt);

            if (isModifierPressed)
            {
                // ★ 触发 Filee 转换轮盘 (Phase 5)
                ConvertRequested?.Invoke(this, Item);
            }
            else
            {
                // 原生系统文件拖放 (Drag-Out 联动收纳篮与桌面)
                CardSurface.Opacity = 0.55;
                DragStarted?.Invoke(this, EventArgs.Empty);
                try
                {
                    var data = new DataObject();
                    data.Set(DataFormats.Files, new[] { Item.FilePath });
                    data.Set(DataFormats.FileNames, new[] { Item.FilePath });
                    data.Set(Clothesline.App.Services.DragDropHelper.RopeItemIdFormat, Item.Id);
                    data.Set(Clothesline.App.Services.DragDropHelper.CardFilePathFormat, Item.FilePath);
                    await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Move);
                }
                catch (Exception ex)
                {
                    // async void 中的异常会终止进程
                    System.Diagnostics.Trace.WriteLine($"[Clothesline] drag failed: {ex}");
                }
                finally
                {
                    CardSurface.Opacity = 1.0;
                }
            }

            _isPressed = false;
            _dragInitiated = false;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.Source == UnhookButton || (e.Source is Visual v && v.FindAncestorOfType<Button>() == UnhookButton))
        {
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount >= 2)
            {
                // 双击：直接使用系统关联程序打开文件！
                PlatformProvider.Current.OpenFile(Item.FilePath);
                ShowToast("正在打开文件");
                e.Handled = true;
                _isPressed = false;
                return;
            }

            _isPressed = true;
            _dragInitiated = false;
            _pointerDownPoint = e.GetPosition(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isPressed && !_dragInitiated)
        {
            // 单击：直接复制文件到系统剪贴板！
            CopyFileToClipboard();
            e.Handled = true;
        }

        _isPressed = false;
        _dragInitiated = false;
    }

    /// <summary>
    /// 每帧更新卡片物理运动与贝塞尔锚定定位
    /// </summary>
    /// <returns>卡片所有弹簧是否均已静止</returns>
    public bool StepPhysics(double dt, double ropeY)
    {
        XMotion.Step(dt);
        DropMotion.Step(dt);
        SwingMotion.Step(dt);
        ScaleMotion.Step(dt);

        Canvas.SetLeft(this, XMotion.Position);
        Canvas.SetTop(this, ropeY + DropMotion.Position);

        // 旋转轴心点在卡片顶部夹子咬合点 (74, 0)；仅更新复用变换的数值
        _scaleTransform.ScaleX = ScaleMotion.Position;
        _scaleTransform.ScaleY = ScaleMotion.Position;
        _rotateTransform.Angle = SwingMotion.Position;

        return XMotion.Settled && DropMotion.Settled && SwingMotion.Settled && ScaleMotion.Settled;
    }
}
