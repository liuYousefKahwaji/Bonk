using System.Windows;
using System.Windows.Media;
using Bonk.Core.Detection;

namespace Bonk.App.UI;

public sealed class LevelHistory : FrameworkElement
{
    private readonly float[] _peaks = new float[180];
    private int _head;
    private DetectionSnapshot _snapshot;
    private static readonly Pen PeakPen = new(new SolidColorBrush(Color.FromRgb(255, 189, 105)), 1.8);
    private static readonly Pen NoisePen = new(new SolidColorBrush(Color.FromRgb(110, 172, 163)), 1);
    private static readonly Pen ThresholdPen = new(new SolidColorBrush(Color.FromRgb(163, 174, 187)), 1) { DashStyle = DashStyles.Dash };
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromRgb(42, 50, 60)), 1);

    public void Push(DetectionSnapshot snapshot)
    {
        _snapshot = snapshot;
        _peaks[_head] = snapshot.Peak;
        _head = (_head + 1) % _peaks.Length;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 1 || height < 1) return;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(20, 25, 31)), null, new Rect(0, 0, width, height), 6, 6);
        for (var i = 1; i < 4; i++) dc.DrawLine(GridPen, new(0, height * i / 4), new(width, height * i / 4));
        // Square-root display gives quiet room levels useful space on the graph.
        double Y(float value) => height - 5 - Math.Sqrt(Math.Clamp(value, 0, 1)) * (height - 10);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new(0, Y(_peaks[_head])), false, false);
            for (var i = 1; i < _peaks.Length; i++)
                context.LineTo(new(width * i / (_peaks.Length - 1), Y(_peaks[(_head + i) % _peaks.Length])), true, false);
        }
        dc.DrawGeometry(null, PeakPen, geometry);
        dc.DrawLine(ThresholdPen, new(0, Y(_snapshot.Threshold)), new(width, Y(_snapshot.Threshold)));
        dc.DrawLine(NoisePen, new(0, Y(_snapshot.NoiseFloor)), new(width, Y(_snapshot.NoiseFloor)));
    }
}
