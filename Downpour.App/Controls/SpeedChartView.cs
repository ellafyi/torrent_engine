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
        canvas.Clear(SKColors.Transparent);

        var dl = DownloadSamples;
        var ul = UploadSamples;
        if (dl == null || dl.Count < 2) return;

        float w = e.Info.Width;
        float h = e.Info.Height;

        var allSamples = ul != null ? dl.Concat(ul) : dl;
        long maxVal = Math.Max(allSamples.DefaultIfEmpty(0).Max(), 1);

        DrawArea(canvas, dl, w, h, maxVal,
            new SKColor(0x2E, 0x8B, 0x57, 0x55),
            new SKColor(0x2E, 0x8B, 0x57));

        if (ul != null && ul.Count >= 2)
            DrawArea(canvas, ul, w, h, maxVal,
                new SKColor(0x15, 0x65, 0xC0, 0x55),
                new SKColor(0x15, 0x65, 0xC0));
    }

    private static void DrawArea(SKCanvas canvas, IReadOnlyList<long> samples,
        float w, float h, long maxVal, SKColor fill, SKColor stroke)
    {
        int n = samples.Count;
        float step = w / (n - 1);

        using var path = new SKPath();
        path.MoveTo(0, h - (samples[0] / (float)maxVal) * h);

        for (int i = 1; i < n; i++)
            path.LineTo(i * step, h - (samples[i] / (float)maxVal) * h);

        path.LineTo(w, h);
        path.LineTo(0, h);
        path.Close();

        using var fillPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = fill
        };
        using var linePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = stroke,
            StrokeWidth = 1.5f
        };

        canvas.DrawPath(path, fillPaint);

        using var linePath = new SKPath();
        linePath.MoveTo(0, h - (samples[0] / (float)maxVal) * h);
        for (int i = 1; i < n; i++)
            linePath.LineTo(i * step, h - (samples[i] / (float)maxVal) * h);

        canvas.DrawPath(linePath, linePaint);
    }
}
