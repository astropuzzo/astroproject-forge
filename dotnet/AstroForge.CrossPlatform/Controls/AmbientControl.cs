using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>
/// A control with a slow ambient clock (30 fps) for idle motion: breathing glows, light flowing along paths.
/// The clock only ticks while the control is on screen, the window is focused and motion is not reduced.
/// </summary>
public abstract class AmbientControl : Control
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private DateTime _last;

    protected AmbientControl() => _timer.Tick += (_, _) => Tick();

    /// <summary>Seconds of ambient time elapsed; frozen while the clock is idle.</summary>
    protected double Clock { get; private set; }

    /// <summary>Whether there is anything to animate right now.</summary>
    protected virtual bool WantsAmbient => true;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _last = DateTime.UtcNow;
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        var dt = Math.Min(0.1, (now - _last).TotalSeconds);
        _last = now;
        if (Motion.Reduced || !IsEffectivelyVisible || !WantsAmbient || TopLevel.GetTopLevel(this) is Window { IsActive: false }) return;
        Clock += dt;
        InvalidateVisual();
    }
}
