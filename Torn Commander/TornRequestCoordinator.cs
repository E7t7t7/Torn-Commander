namespace TornCommander;

public sealed class TornApiCooldownException : HttpRequestException
{
    public TornApiCooldownException(DateTimeOffset until)
        : base($"Torn API cooldown until {until.ToLocalTime():HH:mm:ss}. Requests paused after too many requests; previous data remains available.") { }
}
public sealed class TornRequestCoordinator
{
    public static TornRequestCoordinator Shared { get; } = new();
    private readonly SemaphoreSlim _gate = new(1);
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _spacing;
    private DateTimeOffset _nextStart = DateTimeOffset.MinValue;
    private long _cooldownTicks;
    private int _queued;
    public TornRequestCoordinator(TimeProvider? clock = null, Func<TimeSpan,CancellationToken,Task>? delay = null, TimeSpan? spacing = null)
    {
        _clock = clock ?? TimeProvider.System;
        _delay = delay ?? ((duration, ct) => Task.Delay(duration, ct));
        _spacing = spacing ?? TimeSpan.FromMilliseconds(1250);
    }
    public DateTimeOffset UtcNow => _clock.GetUtcNow();
    public int Queued => Volatile.Read(ref _queued);
    public DateTimeOffset CooldownUntil => new(Interlocked.Read(ref _cooldownTicks), TimeSpan.Zero);
    public int CooldownSeconds => Math.Max(0, (int)Math.Ceiling((CooldownUntil - _clock.GetUtcNow()).TotalSeconds));
    public void ThrowIfCoolingDown()
    {
        if (CooldownUntil > _clock.GetUtcNow()) throw new TornApiCooldownException(CooldownUntil);
    }
    public TornApiCooldownException RateLimited(TimeSpan? retryAfter = null)
    {
        var duration = retryAfter is { } supplied && supplied > TimeSpan.FromSeconds(65) ? supplied : TimeSpan.FromSeconds(65);
        var until = _clock.GetUtcNow().Add(duration).UtcTicks;
        long prior;
        do { prior = Interlocked.Read(ref _cooldownTicks); if (prior >= until) break; }
        while (Interlocked.CompareExchange(ref _cooldownTicks, until, prior) != prior);
        return new(CooldownUntil);
    }
    public async Task<T> RunAsync<T>(Func<CancellationToken,Task<T>> send, CancellationToken ct = default)
    {
        ThrowIfCoolingDown();
        Interlocked.Increment(ref _queued);
        try { await _gate.WaitAsync(ct); }
        finally { Interlocked.Decrement(ref _queued); }
        try
        {
            ThrowIfCoolingDown();
            var delay = _nextStart - _clock.GetUtcNow();
            if (delay > TimeSpan.Zero) await _delay(delay, ct);
            ct.ThrowIfCancellationRequested();
            ThrowIfCoolingDown();
            _nextStart = _clock.GetUtcNow() + _spacing;
            return await send(ct);
        }
        finally { _gate.Release(); }
    }
}
