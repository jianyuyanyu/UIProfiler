using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Events;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Drawing.Geometries;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace UiProfiler.Overlay;

public partial class OverlayWindow
{
    private const int FreezeThreshold = 100;
    private const int BarWidth = 8;

    private Storyboard? _storyboardInProgress;
    private const int DangerThreshold = 100;
    private readonly ObservableCollection<ObservablePoint> _values = [];
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch = new();
    private readonly Stopwatch _totalStopwatch = new();
    private long _total;

    public OverlayWindow()
    {
        DataContext = this;
        InitializeChart();
        InitializeComponent();

        var axis = (Axis)Y[0];
        axis.CustomSeparators = [0, 50, 100, 150];
        axis.LabelsAlignment = LiveChartsCore.Drawing.Align.End;
        var paint = new SolidColorPaint(SKColors.LightGray)
        {
            SKTypeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
            ZIndex = 10
        };
        
        axis.LabelsPaint = paint;
        axis.Position = LiveChartsCore.Measure.AxisPosition.End;
        axis.InLineNamePlacement = false;
        axis.Padding = new(-50, 20, 5, 0);

        TextThreshold.Text = $"Total UI freezes > {FreezeThreshold} ms: ";

        var caption = Environment.GetEnvironmentVariable("PROFILER_OVERLAY_CAPTION");
        if (!string.IsNullOrEmpty(caption))
        {
            TextCaption.Text = caption;
        }

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };

        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var elapsed = _stopwatch.ElapsedMilliseconds;

        if (_stopwatch.IsRunning)
        {
            _stopwatch.Restart();
        }
        else
        {
            _stopwatch.Reset();
        }

        TextTotalTime.Text = _total.ToString();

        _values.Add(new(X[0].MaxLimit, elapsed));
        X[0].MaxLimit++;
        X[0].MinLimit++;

        while (_values[0].X < X[0].MinLimit)
        {
            _values.RemoveAt(0);
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        var width = Chart.ActualWidth;
        var maxElements = width / BarWidth;

        X[0].MaxLimit = maxElements;

        base.OnRenderSizeChanged(sizeInfo);
    }

    public ICartesianAxis[] X { get; set; } = [
        new Axis { MinLimit = 0, MaxLimit = 1, LabelsPaint = null, ShowSeparatorLines = false }
    ];

    public ICartesianAxis[] Y { get; set; } = [
        new Axis { MinLimit = 0, MaxLimit = 150, ShowSeparatorLines = true }
    ];

    public ISeries[] Series { get; set; }

    public RectangularSection[] Sections { get; set; } = [
        new()
        {
            LabelSize = 15,
            LabelPaint = new SolidColorPaint(SKColors.Red)
            {
                SKTypeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
            },
            Yj = DangerThreshold,
            Fill = new SolidColorPaint(SKColors.Red.WithAlpha(50))
        }
    ];

    private void InitializeChart()
    {
        var dangerPaint = new SolidColorPaint(SKColors.Red);

        var series = new ColumnSeries<ObservablePoint, RectangleGeometry>
        {
            Values = _values
        };

        series.Padding = 0;
        series.MaxBarWidth = BarWidth;

        series
            .OnPointMeasured(point =>
            {
                if (point.Visual is null)
                {
                    return;
                }

                var isDanger = point.Model.Y > DangerThreshold;

                point.Visual.Fill = isDanger
                    ? dangerPaint
                    : null; // when null, the series fill is used // mark
            });

        Series = [series];
    }

    public void UpdateResponsiveness(bool isResponsive, long elapsedTime)
    {
        if (isResponsive)
        {
            _stopwatch.Stop();
            _totalStopwatch.Stop();

            if (elapsedTime >= FreezeThreshold)
            {
                _total += elapsedTime;
            }

            if (_storyboardInProgress != null)
            {
                _storyboardInProgress.Stop();
                _storyboardInProgress = null;
            }
        }
        else
        {
            _stopwatch.Start();
            _totalStopwatch.Restart();
            if (_storyboardInProgress == null)
            {
                _storyboardInProgress = (Storyboard)Resources["ShowStoryboard"]!;
                _storyboardInProgress.Begin();
            }
        }
    }

    public void Show(nint window)
    {
        // Get bounding rectangle in device coordinates
        var hr = NativeMethods.DwmGetWindowAttribute(
            window,
            NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out NativeMethods.RECT rect,
            Marshal.SizeOf(typeof(NativeMethods.RECT)));

        if (hr != 0)
        {
            return;
        }

        int windowWidthPx = rect.Right - rect.Left;
        int windowHeightPx = rect.Bottom - rect.Top;

        if (windowWidthPx <= 0 || windowHeightPx <= 0)
        {
            return;
        }

        // Get DPI of the window
        var dpi = NativeMethods.GetDpiForWindow(window);

        // Convert device pixels -> WPF device-independent pixels (DIPs)
        // 1 DIP = 1 px at 96 DPI. So the scale factor is (96 / actualDPI)
        double scale = 96.0 / dpi;

        Left = rect.Left * scale;
        Top = rect.Top * scale;
        Width = windowWidthPx * scale;
        Height = windowHeightPx * scale;

        Show();
    }
}
