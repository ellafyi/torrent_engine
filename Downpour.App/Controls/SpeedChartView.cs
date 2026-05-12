using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Downpour.App.Controls;

public class SpeedChartView : SKCanvasView
{
    public static readonly BindableProperty DownloadSamplesProperty =
        BindableProperty.Create(nameof(DownloadSamples), typeof(IReadOnlyList<long>), typeof(SpeedChartView),
            null, propertyChanged: (b, _, _) => ((SpeedChartView)b).InvalidateSurface());

    public static readonly BindableProperty UploadSamplesProperty =
        BindableProperty.Create(nameof(UploadSamples), typeof(IReadOnlyList<long>), typeof(SpeedChartView),
            null, propertyChanged: (b, _, _) => ((SpeedChartView)b).InvalidateSurface());

    public IReadOnlyList<long>? DownloadSamples
    {
        get => (IReadOnlyList<long>?)GetValue(DownloadSamplesProperty);
        set => SetValue(DownloadSamplesProperty, value);
    }

    public IReadOnlyList<long>? UploadSamples
    {
        get => (IReadOnlyList<long>?)GetValue(UploadSamplesProperty);
        set => SetValue(UploadSamplesProperty, value);
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float w = e.Info.Width;
        float h = e.Info.Height;

        // Clear the canvas to fully transparent before painting the gradient to prevent ghosting
        canvas.Clear(SKColors.Transparent);

        using (var bgShader = SKShader.CreateLinearGradient(
                   new SKPoint(0, 0),
                   new SKPoint(0, h),
                   new[] { new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 25) },
                   SKShaderTileMode.Clamp))
        using (var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Shader = bgShader })
        {
            canvas.DrawRect(0, 0, w, h, bgPaint);
        }

        var dl = DownloadSamples;
        var ul = UploadSamples;
        if (dl == null || dl.Count < 2) return;

        var allSamples = ul != null ? dl.Concat(ul) : dl;
        long maxVal = Math.Max(allSamples.DefaultIfEmpty(0).Max(), 1);

        const float topPad = 4f;
        float drawH = h - topPad;

        // Reserve left margin sized to the widest scale label
        float textSize = Math.Clamp(h * 0.13f, 12f, 20f);
        float labelW;
        using (var font = new SKFont { Size = textSize })
            labelW = font.MeasureText(FormatSpeed(maxVal)) + 10f;

        float chartX = labelW;
        float chartW = w - labelW;

        DrawScale(canvas, maxVal, chartX, chartW, h, drawH, topPad, textSize);

        DrawArea(canvas, dl, chartX, chartW, h, drawH, topPad, maxVal,
            new SKColor(0x2E, 0x8B, 0x57));

        if (ul != null && ul.Count >= 2)
            DrawArea(canvas, ul, chartX, chartW, h, drawH, topPad, maxVal,
                new SKColor(0x15, 0x65, 0xC0));
    }

    private static void DrawScale(SKCanvas canvas, long maxVal,
        float chartX, float chartW, float h, float drawH, float topPad, float textSize)
    {
        using var gridPaint = new SKPaint
        {
            Color = new SKColor(128, 128, 128, 35),
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke
        };

        using var font = new SKFont { Size = textSize };
        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(200, 200, 200, 160)
        };

        foreach (float frac in new[] { 1f, 0.5f, 0f })
        {
            float y = topPad + (1f - frac) * drawH;
            canvas.DrawLine(chartX, y, chartX + chartW, y, gridPaint);

            string label = FormatSpeed((long)(maxVal * frac));
            font.MeasureText(label, out SKRect bounds);

            float baseline = Math.Clamp(y - bounds.MidY, -bounds.Top, h - bounds.Bottom);
            canvas.DrawText(label, chartX - bounds.Width - 4f, baseline, SKTextAlign.Left, font, textPaint);
        }
    }

    private static void DrawArea(SKCanvas canvas, IReadOnlyList<long> samples,
        float chartX, float chartW, float h, float drawH, float topPad,
        long maxVal, SKColor lineColor)
    {
        int n = samples.Count;
        float step = chartW / (n - 1);

        float Y(int i) => topPad + (1f - samples[i] / (float)maxVal) * drawH;

        // Filled area with gradient fading from opaque near the top to transparent at the bottom
        using var fillPath = new SKPath();
        fillPath.MoveTo(chartX, Y(0));
        for (int i = 1; i < n; i++)
            fillPath.LineTo(chartX + i * step, Y(i));
        fillPath.LineTo(chartX + chartW, h);
        fillPath.LineTo(chartX, h);
        fillPath.Close();

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0f, topPad),
            new SKPoint(0f, h),
            new[] { new SKColor(lineColor.Red, lineColor.Green, lineColor.Blue, 0xA0),
                    new SKColor(lineColor.Red, lineColor.Green, lineColor.Blue, 0x00) },
            SKShaderTileMode.Clamp
        );
        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Shader = shader };
        canvas.DrawPath(fillPath, fillPaint);

        // Stroke line on top
        using var linePath = new SKPath();
        linePath.MoveTo(chartX, Y(0));
        for (int i = 1; i < n; i++)
            linePath.LineTo(chartX + i * step, Y(i));

        using var linePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(lineColor.Red, lineColor.Green, lineColor.Blue, 0xFF),
            StrokeWidth = 2f
        };
        canvas.DrawPath(linePath, linePaint);
    }

    private static string FormatSpeed(long bps) => bps switch
    {
        >= 1_000_000 => $"{bps / 1_000_000.0:F1} MB/s",
        >= 1_000     => $"{bps / 1_000.0:F1} KB/s",
        _            => $"{bps} B/s"
    };
}
