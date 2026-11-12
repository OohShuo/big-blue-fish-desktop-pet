using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BigBlueFish;

internal sealed class LinuxBubble : Decorator
{
    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width - 1.5, h = Bounds.Height - 1.5, s = Padding.Left / 16, r = 24 * s, tail = 12 * s, body = h - tail, x = w * .65;
        if (w > r * 2 && body > r * 2)
        {
            var path = Geometry.Parse(FormattableString.Invariant($"M {r},0.75 H {w - r} Q {w},0.75 {w},{r} V {body - r} Q {w},{body} {w - r},{body} H {x + tail} L {x},{h} L {x - tail},{body} H {r} Q 0.75,{body} 0.75,{body - r} V {r} Q 0.75,0.75 {r},0.75 Z"));
            context.DrawGeometry(new SolidColorBrush(Color.FromRgb(255, 251, 244)), new Pen(new SolidColorBrush(Color.FromRgb(80, 96, 152)), 1.5 * s), path);
        }
        base.Render(context);
    }
}
