using Avalonia;
using Avalonia.Animation;
using Avalonia.Media;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>
/// Workspace switch: the old page dims away while the new one drifts in from the slew direction
/// and settles, in step with the star-streak warp behind it.
/// </summary>
public sealed class SlewPageTransition : IPageTransition
{
    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        var host = (to ?? from) is { } visual ? Avalonia.Controls.TopLevel.GetTopLevel(visual) : null;
        if (Motion.Reduced || host is null)
        {
            if (from is not null) from.IsVisible = false;
            if (to is not null) { to.IsVisible = true; to.Opacity = 1; to.RenderTransform = null; }
            return;
        }

        var direction = forward ? 1 : -1;
        var shift = new TranslateTransform();
        if (to is not null)
        {
            to.Opacity = 0;
            to.RenderTransform = shift;
            to.IsVisible = true;
        }

        var outgoing = from is null ? Task.CompletedTask : Motion.Tween(host, TimeSpan.FromMilliseconds(140), t => t, t =>
        {
            if (!cancellationToken.IsCancellationRequested) from.Opacity = 1 - t;
        });
        var incoming = to is null ? Task.CompletedTask : Motion.Tween(host, TimeSpan.FromMilliseconds(460), Motion.EaseOutExpo, t =>
        {
            if (cancellationToken.IsCancellationRequested) return;
            to.Opacity = Math.Min(1, t * 1.6);
            shift.X = direction * 34 * (1 - t);
        }, TimeSpan.FromMilliseconds(70));
        await Task.WhenAll(outgoing, incoming);

        if (from is not null)
        {
            from.IsVisible = false;
            from.Opacity = 1;
        }
        if (to is not null)
        {
            to.Opacity = 1;
            to.RenderTransform = null;
        }
    }
}
