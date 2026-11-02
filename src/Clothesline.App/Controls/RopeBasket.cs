using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Clothesline.Core.Physics;

namespace Clothesline.App.Controls;

/// <summary>
/// StyleKit 铅笔手绘风（Sketch Style）拟物藤编布艺洗衣篮
/// 具有梯形上宽下窄敞口、翻折纯棉白亚麻布内衬、双耳麻绳提手、细腻藤条交叉排线与牛皮纸便签角标
/// </summary>
public class RopeBasket : Control
{
    public static readonly StyledProperty<int> ItemCountProperty =
        AvaloniaProperty.Register<RopeBasket, int>(nameof(ItemCount), defaultValue: 0);

    public static readonly StyledProperty<bool> IsHighlightedProperty =
        AvaloniaProperty.Register<RopeBasket, bool>(nameof(IsHighlighted), defaultValue: false);

    public int ItemCount
    {
        get => GetValue(ItemCountProperty);
        set => SetValue(ItemCountProperty, value);
    }

    public bool IsHighlighted
    {
        get => GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }

    public readonly SpringMotion SwingMotion = new(response: 0.45, damping: 0.72, initial: 0.0);
    public readonly SpringMotion CounterMotion = new(response: 0.28, damping: 0.65, initial: 1.0);
    public readonly SpringMotion ScaleMotion = new(response: 0.28, damping: 0.65, initial: 1.0);

    public event EventHandler? Clicked;

    private bool _isHovered;

    public RopeBasket()
    {
        Width = 100;
        Height = 106;
        ClipToBounds = false;
        Cursor = new Cursor(StandardCursorType.Hand);
        DragDrop.SetAllowDrop(this, true);
    }

    /// <returns>收纳篮所有弹簧是否均已静止</returns>
    public bool StepPhysics(double dt)
    {
        ScaleMotion.Target = IsHighlighted ? 1.16 : (_isHovered ? 1.05 : 1.0);

        bool wasSettled = SwingMotion.Settled && CounterMotion.Settled && ScaleMotion.Settled;
        SwingMotion.Step(dt);
        CounterMotion.Step(dt);
        ScaleMotion.Step(dt);

        if (!wasSettled || !SwingMotion.Settled || !CounterMotion.Settled || !ScaleMotion.Settled)
        {
            InvalidateVisual();
        }

        return SwingMotion.Settled && CounterMotion.Settled && ScaleMotion.Settled;
    }

    /// <summary>
    /// 卡片解挂落入晾衣篮或拖拽收纳时触发的物理弹跳
    /// </summary>
    public void Bounce()
    {
        SwingMotion.Velocity = Math.Max(-32.0, SwingMotion.Velocity - 24.0);
        CounterMotion.Velocity = Math.Min(2.5, CounterMotion.Velocity + 1.8);
        InvalidateVisual();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _isHovered = true;
        SwingMotion.Velocity += 8.0;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _isHovered = false;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        SwingMotion.Velocity += 14.0;
        InvalidateVisual();
        Clicked?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    // ===== 渲染资源缓存 =====
    // 篮子的全部几何形状都是局部坐标系下的常量（摆动/缩放通过 PushTransform 实现），
    // 因此几何、画笔、画刷均只构建一次；动画期间每帧重绘不再产生大量分配与 Color.Parse。
    private static readonly IImmutableBrush InkBrush = new ImmutableSolidColorBrush(Color.Parse("#1E293B"));
    private static readonly IImmutableBrush LinenBrush = new ImmutableSolidColorBrush(Color.Parse("#FFFDF9"));
    private static readonly IImmutableBrush EmptyBadgeBrush = new ImmutableSolidColorBrush(Color.Parse("#EFE7DC"));
    private static readonly IImmutableBrush FillNormal = new ImmutableSolidColorBrush(Color.Parse("#F4ECE1"));
    private static readonly IImmutableBrush FillHovered = new ImmutableSolidColorBrush(Color.Parse("#FDF8F0"));
    private static readonly IImmutableBrush FillHighlighted = new ImmutableSolidColorBrush(Color.Parse("#FFF1D6"));
    private static readonly IPen SketchBorder = new ImmutablePen(InkBrush, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly IPen FineSketch = new ImmutablePen(InkBrush, 1.2, lineCap: PenLineCap.Round);
    private static readonly IPen HandleOuterPen = new ImmutablePen(InkBrush, 2.4, lineCap: PenLineCap.Round);
    private static readonly IPen HandleInnerPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#E2CDAE")), 1.2, lineCap: PenLineCap.Round);
    private static readonly IPen HaloPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(150, 245, 158, 11)), 7.0, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly IPen InnerHaloPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(200, 254, 240, 138)), 3.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly IPen HatchPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(95, 30, 41, 59)), 1.1, lineCap: PenLineCap.Round);
    private static readonly IPen ShadowHatchPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(130, 30, 41, 59)), 1.1, lineCap: PenLineCap.Round);
    private static readonly IPen StitchPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(120, 30, 41, 59)), 1.0, lineCap: PenLineCap.Round);
    private static readonly Typeface BadgeTypeface = new("Inter, Segoe UI, sans-serif", FontStyle.Normal, FontWeight.Bold);

    private static readonly Point Pivot = new(50.0, 2.0); // (Width / 2, 2)，Width 固定为 100
    private static readonly Geometry HookGeometry = BuildHook();
    private static readonly Geometry LeftHandle = BuildCubic(new Point(18, 36), new Point(6, 38), new Point(6, 52), new Point(20, 54));
    private static readonly Geometry RightHandle = BuildCubic(new Point(82, 36), new Point(94, 38), new Point(94, 52), new Point(80, 54));
    private static readonly Geometry BasketBody = BuildBody(0);
    private static readonly Geometry ShadowBody = BuildBody(3.5);
    private static readonly Geometry RibsGeometry;
    private static readonly Geometry HatchGeometry;
    private static readonly Geometry ShadowHatchGeometry = BuildShadowHatch();
    private static readonly Geometry LinerGeometry = BuildLiner(0);
    private static readonly Geometry LinerShadow = BuildLiner(2);
    private static readonly Geometry StitchGeometry = BuildLines(
        (new Point(30, 31), new Point(30, 39)), (new Point(48, 31), new Point(48, 39)), (new Point(66, 31), new Point(66, 39)));

    private FormattedText? _badgeText;
    private int _badgeTextCount = -1;

    static RopeBasket()
    {
        AffectsRender<RopeBasket>(ItemCountProperty, IsHighlightedProperty);
        (RibsGeometry, HatchGeometry) = BuildWicker();
    }

    private static Geometry BuildHook()
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(new Point(Pivot.X + 8, Pivot.Y + 12), false);
        c.ArcTo(new Point(Pivot.X - 8, Pivot.Y + 2), new Size(8, 8), 0, false, SweepDirection.CounterClockwise);
        c.LineTo(new Point(Pivot.X, Pivot.Y + 22));
        c.EndFigure(false);
        return g;
    }

    private static Geometry BuildCubic(Point start, Point c1, Point c2, Point end)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(start, false);
        c.CubicBezierTo(c1, c2, end);
        c.EndFigure(false);
        return g;
    }

    // 梯形收口藤编筐主体 (上宽 66px, 下宽 50px, 高 64px)；offset 用于手绘硬阴影
    private static Geometry BuildBody(double o)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(new Point(17 + o, 28 + o), true);
        c.LineTo(new Point(83 + o, 28 + o));
        c.LineTo(new Point(76 + o, 88 + o));
        c.ArcTo(new Point(70 + o, 93 + o), new Size(5, 5), 0, false, SweepDirection.Clockwise);
        c.LineTo(new Point(30 + o, 93 + o));
        c.ArcTo(new Point(24 + o, 88 + o), new Size(5, 5), 0, false, SweepDirection.Clockwise);
        c.LineTo(new Point(17 + o, 28 + o));
        c.EndFigure(true);
        return g;
    }

    private static (Geometry Ribs, Geometry Hatch) BuildWicker()
    {
        var ribs = new StreamGeometry();
        var hatch = new StreamGeometry();
        using (var rc = ribs.Open())
        using (var hc = hatch.Open())
        {
            foreach (double ribY in new[] { 48.0, 60.0, 72.0, 84.0 })
            {
                double progress = (ribY - 28.0) / (93.0 - 28.0);
                double leftX = 17.0 + (24.0 - 17.0) * progress;
                double rightX = 83.0 - (83.0 - 76.0) * progress;

                rc.BeginFigure(new Point(leftX + 2, ribY), false);
                rc.QuadraticBezierTo(new Point(50, ribY + 1.8), new Point(rightX - 2, ribY));
                rc.EndFigure(false);

                for (double hx = leftX + 4; hx <= rightX - 6; hx += 8.5)
                {
                    hc.BeginFigure(new Point(hx, ribY - 4.5), false);
                    hc.LineTo(new Point(hx + 4.5, ribY + 4.5));
                    hc.EndFigure(false);
                    hc.BeginFigure(new Point(hx + 4.5, ribY - 4.5), false);
                    hc.LineTo(new Point(hx, ribY + 4.5));
                    hc.EndFigure(false);
                }
            }
        }
        return (ribs, hatch);
    }

    private static Geometry BuildShadowHatch()
    {
        var lines = new List<(Point, Point)>();
        for (double sy = 44; sy <= 88; sy += 5)
        {
            lines.Add((new Point(22, sy), new Point(28, sy + 3)));
            lines.Add((new Point(78, sy), new Point(72, sy + 3)));
        }
        return BuildLines(lines.ToArray());
    }

    private static Geometry BuildLines(params (Point From, Point To)[] lines)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        foreach (var (from, to) in lines)
        {
            c.BeginFigure(from, false);
            c.LineTo(to);
            c.EndFigure(false);
        }
        return g;
    }

    // 顶部翻折纯棉白亚麻布内衬，带 4 个柔和布褶下摆；offset 用于布艺阴影
    private static Geometry BuildLiner(double o)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(new Point(14 + o, 25 + o), true);
        c.LineTo(new Point(86 + o, 25 + o));
        c.LineTo(new Point(84 + o, 40 + o));
        c.QuadraticBezierTo(new Point(75 + o, 43 + o), new Point(66 + o, 40 + o));
        c.QuadraticBezierTo(new Point(57 + o, 43 + o), new Point(48 + o, 40 + o));
        c.QuadraticBezierTo(new Point(39 + o, 43 + o), new Point(30 + o, 40 + o));
        c.QuadraticBezierTo(new Point(21 + o, 43 + o), new Point(16 + o, 40 + o));
        c.LineTo(new Point(14 + o, 25 + o));
        c.EndFigure(true);
        return g;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double angle = SwingMotion.Position;
        double scale = ScaleMotion.Position;

        using (context.PushTransform(Matrix.CreateTranslation(-Pivot.X, -Pivot.Y) *
                                     Matrix.CreateScale(scale, scale) *
                                     Matrix.CreateRotation(Math.PI * angle / 180.0) *
                                     Matrix.CreateTranslation(Pivot.X, Pivot.Y)))
        {
            // 1. 顶端手弯金属细铁丝挂钩与吊环
            context.DrawGeometry(null, SketchBorder, HookGeometry);
            context.DrawEllipse(LinenBrush, SketchBorder, new Point(Pivot.X, Pivot.Y + 22), 3.5, 3.5);

            // 2. 左右两侧手工编织麻绳提手
            context.DrawGeometry(null, HandleOuterPen, LeftHandle);
            context.DrawGeometry(null, HandleInnerPen, LeftHandle);
            context.DrawGeometry(null, HandleOuterPen, RightHandle);
            context.DrawGeometry(null, HandleInnerPen, RightHandle);

            // 3. 拖拽靠近时暖阳手绘金光轮廓
            if (IsHighlighted)
            {
                context.DrawGeometry(null, HaloPen, BasketBody);
                context.DrawGeometry(null, InnerHaloPen, BasketBody);
            }

            // 筐体硬阴影 + 竹藤底色
            context.DrawGeometry(InkBrush, null, ShadowBody);
            var fill = IsHighlighted ? FillHighlighted : (_isHovered ? FillHovered : FillNormal);
            context.DrawGeometry(fill, SketchBorder, BasketBody);

            // 4. 交错竹藤编织排线
            context.DrawGeometry(null, FineSketch, RibsGeometry);
            context.DrawGeometry(null, HatchPen, HatchGeometry);
            context.DrawGeometry(null, ShadowHatchPen, ShadowHatchGeometry);

            // 5. 翻折亚麻布内衬
            context.DrawGeometry(InkBrush, null, LinerShadow);
            context.DrawGeometry(LinenBrush, SketchBorder, LinerGeometry);
            context.DrawGeometry(null, StitchPen, StitchGeometry);

            // 6. 牛皮纸便签计数角标
            double badgeScale = Math.Clamp(CounterMotion.Position, 0.5, 2.2);
            var badgePivot = new Point(78, 22);

            using (context.PushTransform(Matrix.CreateTranslation(-badgePivot.X, -badgePivot.Y) *
                                         Matrix.CreateScale(badgeScale, badgeScale) *
                                         Matrix.CreateTranslation(badgePivot.X, badgePivot.Y)))
            {
                int count = ItemCount;
                if (_badgeText == null || _badgeTextCount != count)
                {
                    _badgeTextCount = count;
                    _badgeText = new FormattedText(
                        count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture),
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        BadgeTypeface,
                        11.0,
                        InkBrush);
                }
                var formatted = _badgeText;

                double badgeWidth = Math.Max(22, formatted.Width + 12);
                double badgeHeight = 20;
                var badgeRect = new Rect(badgePivot.X - badgeWidth / 2.0, badgePivot.Y - badgeHeight / 2.0, badgeWidth, badgeHeight);

                context.DrawRectangle(InkBrush, null, new Rect(badgeRect.X + 2.5, badgeRect.Y + 2.5, badgeWidth, badgeHeight), 5, 5);
                context.DrawRectangle(count > 0 ? LinenBrush : EmptyBadgeBrush, SketchBorder, badgeRect, 5, 5);
                context.DrawEllipse(InkBrush, null, new Point(badgeRect.X + 4.5, badgeRect.Y + 4.5), 1.2, 1.2);

                double textX = badgeRect.X + (badgeWidth - formatted.Width) / 2.0;
                double textY = badgeRect.Y + (badgeHeight - formatted.Height) / 2.0;
                context.DrawText(formatted, new Point(textX, textY));
            }
        }
    }
}
