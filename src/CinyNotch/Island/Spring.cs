namespace CinyNotch.Island;

/// <summary>
/// A damped spring (mass 1). Stepped once per rendered frame, so motion follows
/// the monitor's refresh rate instead of a fixed timer.
/// </summary>
internal sealed class Spring(double value)
{
    public double Value { get; private set; } = value;
    public double Velocity { get; private set; }
    public double Target { get; set; } = value;

    // Critically damped (damping = 2 * sqrt(stiffness)): fast, and it settles without
    // overshooting, so the edges never wobble back and forth.
    public double Stiffness { get; init; } = 420;
    public double Damping { get; init; } = 41;

    /// <summary>Advances the spring. Returns false once it has come to rest.</summary>
    public bool Step(double seconds)
    {
        // Fixed small sub-steps keep the spring stable on slow or dropped frames.
        const double sub = 1.0 / 240;
        seconds = Math.Min(seconds, 0.05);
        while (seconds > 0)
        {
            var dt = Math.Min(sub, seconds);
            var force = -Stiffness * (Value - Target) - Damping * Velocity;
            Velocity += force * dt;
            Value += Velocity * dt;
            seconds -= dt;
        }

        if (Math.Abs(Value - Target) < 0.25 && Math.Abs(Velocity) < 0.5)
        {
            Value = Target;
            Velocity = 0;
            return false;
        }
        return true;
    }
}
