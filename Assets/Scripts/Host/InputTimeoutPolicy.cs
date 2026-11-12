using System;

public enum InputTimeoutMode
{
    Fixed,
    Adaptive
}

public sealed class InputTimeoutPolicy
{
    private const int WarmupIntervals = 4;
    private readonly InputTimeoutMode mode;
    private readonly float fixedSeconds;
    private readonly float minSeconds;
    private readonly float maxSeconds;
    private int samples;
    private float mean;
    private float deviation;

    public bool HasInput { get; private set; }
    public float LastArrival { get; private set; }
    public float TimeoutSeconds { get; private set; }

    public InputTimeoutPolicy(InputTimeoutMode mode, float fixedSeconds, float minSeconds, float maxSeconds)
    {
        this.mode = mode;
        this.fixedSeconds = Math.Max(0.001f, fixedSeconds);
        this.minSeconds = Math.Max(0.001f, minSeconds);
        this.maxSeconds = Math.Max(this.minSeconds, maxSeconds);
        Reset();
    }

    public void Reset()
    {
        HasInput = false;
        LastArrival = 0f;
        samples = 0;
        mean = deviation = 0f;
        TimeoutSeconds = mode == InputTimeoutMode.Adaptive ? Clamp(fixedSeconds) : fixedSeconds;
    }

    public bool IsExpired(float now) => HasInput && now - LastArrival > TimeoutSeconds;

    public float ObserveArrival(float now)
    {
        float interval = HasInput ? now - LastArrival : float.NaN;
        // Ignore deliveries in the same frame and gaps that already expired.
        if (mode == InputTimeoutMode.Adaptive && interval > 0f && !IsExpired(now))
        {
            if (samples == 0)
            {
                mean = interval;
                deviation = interval / 2f;
            }
            else
            {
                deviation += (Math.Abs(interval - mean) - deviation) / 4f;
                mean += (interval - mean) / 8f;
            }

            if (samples < WarmupIntervals) samples++;
            if (samples == WarmupIntervals) TimeoutSeconds = Clamp(mean + 4f * deviation);
        }

        LastArrival = now;
        HasInput = true;
        return interval;
    }

    private float Clamp(float seconds) => Math.Min(maxSeconds, Math.Max(minSeconds, seconds));
}
