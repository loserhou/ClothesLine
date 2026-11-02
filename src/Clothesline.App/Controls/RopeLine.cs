using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Clothesline.App.Controls;

/// <summary>
/// StyleKit 铅笔手绘风（Sketch Style）贝塞尔物理晾衣绳
/// </summary>
public class RopeLine : Control
{
    public static readonly StyledProperty<double> SagProperty =
        AvaloniaProperty.Register<RopeLine, double>(nameof(Sag), defaultValue: 18.0);

    public static readonly StyledProperty<Color> RopeColorProperty =
        AvaloniaProperty.Register<RopeLine, Color>(nameof(RopeColor), defaultValue: Color.Parse("#8B7355"));

    public static readonly StyledProperty<double> RopeThicknessProperty =
        AvaloniaProperty.Register<RopeLine, double>(nameof(RopeThickness), defaultValue: 3.2);

    public static readonly StyledProperty<bool> IsHighlightedProperty =
        AvaloniaProperty.Register<RopeLine, bool>(nameof(IsHighlighted), defaultValue: false);

    public double Sag
    {
        get => GetValue(SagProperty);
        set => SetValue(SagProperty, value);
    }

    public Color RopeColor
    {
        get => GetValue(RopeColorProperty);
        set => SetValue(RopeColorProperty, value);
    }

    public double RopeThickness
    {
        get => GetValue(RopeThicknessProperty);
        set => SetValue(RopeThicknessProperty, value);
    }

    public bool IsHighlighted
    {
        get => GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }

    static RopeLine()
    {
        AffectsRender<RopeLine>(SagProperty, RopeColorProperty, RopeThicknessProperty, IsHighlightedProperty);
    }

    public RopeLine()
    {
        IsHitTestVisible = false;
    }

    /// <summary>
    /// 根据卡片横坐标获取当前绳索的精确物理 Y 高度
    /// </summary>
    public double GetRopeYAt(double x, double totalWidth)
    {
        if (totalWidth <= 0) return 18.0;
        double fraction = Math.Clamp(x / totalWidth, 0.0, 1.0);
        // 抛物线下垂公式: 4 * Sag * fraction * (1 - fraction)
        double sagOffset = 4.0 * Sag * fraction * (1.0 - fraction);
        return 18.0 + sagOffset;
    }

    // 渲染资源全部缓存为不可变对象：绳索在动画期间每帧重绘，避免每帧 Color.Parse 与画笔/画刷分配
    private const double StartY = 18.0;
    private static readonly IPen ShadowPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(45, 30, 41, 59)), 3.2, lineCap: PenLineCap.Round);
    private static readonly IImmutableBrush GraphiteBrush = new ImmutableSolidColorBrush(Color.Parse("#1E293B"));
    private static readonly IImmutableBrush CoreBrush = new ImmutableSolidColorBrush(Color.Parse("#E2CDAE"));
    private static readonly IImmutableBrush CoreHighlightBrush = new ImmutableSolidColorBrush(Color.Parse("#FFF0D4"));
    private static readonly IImmutableBrush GlowBrush = new ImmutableSolidColorBrush(Color.FromArgb(140, 245, 158, 11));
    private static readonly IImmutableBrush InnerGlowBrush = new ImmutableSolidColorBrush(Color.FromArgb(200, 254, 240, 138));
    private static readonly IPen TwistPen = new ImmutablePen(GraphiteBrush, 1.0, new ImmutableDashStyle(new double[] { 4, 6 }, 0), PenLineCap.Round);

    private IPen? _outlinePen, _outlinePenHighlighted, _corePen, _corePenHighlighted, _glowPen, _innerGlowPen, _shadowPen;
    private double _pensThickness = double.NaN;

    private void EnsurePens()
    {
        double t = RopeThickness;
        if (t == _pensThickness) return;
        _pensThickness = t;
        _outlinePen = new ImmutablePen(GraphiteBrush, t + 1.2, lineCap: PenLineCap.Round);
        _outlinePenHighlighted = new ImmutablePen(GraphiteBrush, t + 1.8, lineCap: PenLineCap.Round);
        _corePen = new ImmutablePen(CoreBrush, t - 0.8, lineCap: PenLineCap.Round);
        _corePenHighlighted = new ImmutablePen(CoreHighlightBrush, t - 0.4, lineCap: PenLineCap.Round);
        _glowPen = new ImmutablePen(GlowBrush, t + 8.0, lineCap: PenLineCap.Round);
        _innerGlowPen = new ImmutablePen(InnerGlowBrush, t + 3.2, lineCap: PenLineCap.Round);
        _shadowPen = t == 3.2 ? ShadowPen : new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(45, 30, 41, 59)), t, lineCap: PenLineCap.Round);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Bounds.Width;
        if (width <= 0) return;

        EnsurePens();
        bool highlighted = IsHighlighted;

        // 主麻绳几何路径（阴影与拧绳排线复用同一几何，仅做垂直平移）
        var outlineGeometry = new StreamGeometry();
        using (var ctx = outlineGeometry.Open())
        {
            ctx.BeginFigure(new Point(0, StartY), false);
            ctx.QuadraticBezierTo(new Point(width / 2.0, StartY + 2.0 * Sag), new Point(width, StartY));
            ctx.EndFigure(false);
        }

        // 0. 拖拽悬停高亮提示：温暖手绘素描金黄光晕 (优雅指示可挂绳有效区域)
        if (highlighted)
        {
            context.DrawGeometry(null, _glowPen, outlineGeometry);
            context.DrawGeometry(null, _innerGlowPen, outlineGeometry);
        }

        // 1. 底层硬边缘手绘阴影 (2.2px 偏移)
        using (context.PushTransform(Matrix.CreateTranslation(0, 2.2)))
        {
            context.DrawGeometry(null, _shadowPen, outlineGeometry);
        }

        // 2. 主麻绳石墨勾线 (手绘深铅笔灰)
        context.DrawGeometry(null, highlighted ? _outlinePenHighlighted : _outlinePen, outlineGeometry);

        // 3. 绳身双股天然麻芯 (暖麻色，高亮时微亮变金黄麦草色)
        context.DrawGeometry(null, highlighted ? _corePenHighlighted : _corePen, outlineGeometry);

        // 4. 麻绳螺旋手绘断线点缀 (素描拧绳排线)
        using (context.PushTransform(Matrix.CreateTranslation(0, -0.4)))
        {
            context.DrawGeometry(null, TwistPen, outlineGeometry);
        }
    }
}
