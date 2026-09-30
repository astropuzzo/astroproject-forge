using Avalonia.Controls;
using Avalonia.Threading;

namespace AstroForge.CrossPlatform;

/// <summary>
/// Small frame-driven tween helper shared by the entrance, the slew warp and the sliding pill.
/// Every animation collapses to its end state when <see cref="Reduced"/> is on.
/// </summary>
internal static class Motion
{
    public static bool Reduced { get; set; }

    public static event EventHandler? ReducedChanged;

    public static void SetReduced(bool reduced)
    {
        if (Reduced == reduced) return;
        Reduced = reduced;
        ReducedChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Runs <paramref name="step"/> with an eased 0→1 progress on every rendered frame.</summary>
    public static Task Tween(TopLevel? host, TimeSpan duration, Func<double, double> ease, Action<double> step, TimeSpan delay = default)
    {
        if (Reduced || host is null || duration <= TimeSpan.Zero)
        {
            step(1);
            return Task.CompletedTask;
        }

        var done = new TaskCompletionSource();
        TimeSpan? start = null;
        void Frame(TimeSpan now)
        {
            start ??= now + delay;
            var progress = Math.Clamp((now - start.Value).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            step(ease(progress));
            if (progress < 1 && !Reduced) host.RequestAnimationFrame(Frame);
            else
            {
                if (progress < 1) step(1);
                done.TrySetResult();
            }
        }
        Dispatcher.UIThread.Post(() => host.RequestAnimationFrame(Frame));
        return done.Task;
    }

    public static double EaseOutExpo(double t) => t >= 1 ? 1 : 1 - Math.Pow(2, -10 * t);
    public static double EaseInOutCubic(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    public static double EaseOutBack(double t)
    {
        const double c1 = 1.70158, c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }

    /// <summary>Critically damped spring step: moves <paramref name="value"/> toward <paramref name="target"/>.</summary>
    public static double Spring(double value, ref double velocity, double target, double dt, double stiffness = 170, double damping = 20)
    {
        var force = stiffness * (target - value) - damping * velocity;
        velocity += force * dt;
        return value + velocity * dt;
    }
}
