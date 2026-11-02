using Avalonia;
using Avalonia.Controls;
using Clothesline.App.Services;
using Clothesline.Core.Models;
using Clothesline.Platform;

namespace Clothesline.App.Views;

public partial class HoverPreviewWindow : Window
{
    public HoverPreviewWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var handle = TryGetPlatformHandle()?.Handle;
        if (handle.HasValue && handle.Value != IntPtr.Zero)
        {
            // 预览窗口鼠标穿透，防止遮挡鼠标交互
            PlatformProvider.Current.SetWindowPassMouse(handle.Value, true);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        // 预览位图最大 640px 宽，每次悬停都会新解码一张；窗口关闭即释放，避免依赖终结器回收原生内存
        if (PreviewImage.Source is Avalonia.Media.Imaging.Bitmap bitmap)
        {
            PreviewImage.Source = null;
            bitmap.Dispose();
        }
    }

    public void ApplyPreview(PreviewResult result)
    {
        TitleText.Text = result.Title;
        MetaText.Text = result.MetaInfo;

        ImageContainer.IsVisible = false;
        TextContainer.IsVisible = false;
        DetailsContainer.IsVisible = false;

        if (result.Kind == ItemKind.Image && result.Image != null)
        {
            PreviewImage.Source = result.Image;
            ImageContainer.IsVisible = true;
        }
        else if (result.TextLines != null)
        {
            CodeText.Text = result.TextLines;
            TextContainer.IsVisible = true;
        }

        if (!string.IsNullOrWhiteSpace(result.AdditionalDetails))
        {
            DetailsText.Text = result.AdditionalDetails;
            DetailsContainer.IsVisible = true;
        }
    }

    public void PositionNearAnchor(PixelPoint anchorScreenBottomCenter, PixelRect workingArea)
    {
        // 确保布局完成以获取准确的测量尺寸
        Measure(Size.Infinity);
        var desired = DesiredSize;
        double w = desired.Width > 0 ? desired.Width : 400.0;
        double h = desired.Height > 0 ? desired.Height : 260.0;

        int targetX = (int)(anchorScreenBottomCenter.X - w / 2.0);
        int targetY = anchorScreenBottomCenter.Y + 10;

        // 智能屏幕边缘贴边防出界
        int minX = workingArea.X + 16;
        int maxX = workingArea.X + workingArea.Width - (int)w - 16;
        int clampedX = Math.Clamp(targetX, minX, Math.Max(minX, maxX));

        int maxY = workingArea.Y + workingArea.Height - (int)h - 16;
        int clampedY = Math.Min(targetY, maxY);

        Position = new PixelPoint(clampedX, clampedY);
    }
}
