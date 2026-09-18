namespace AiRepoKit.Agents.Runtime.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;
    private readonly List<ManualTimer> _timers = [];
    private readonly object _lock = new();

    public ManualTimeProvider(DateTimeOffset? initialTime = null)
    {
        this._utcNow = initialTime ?? new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (this._lock)
        {
            return this._utcNow;
        }
    }

    public override long GetTimestamp()
    {
        lock (this._lock)
        {
            return this._utcNow.Ticks;
        }
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ManualTimer timer = new(this, callback, state, dueTime, period);
        lock (this._lock)
        {
            this._timers.Add(timer);
        }
        return timer;
    }

    public void Advance(TimeSpan duration)
    {
        List<ManualTimer> timersToFire = [];
        lock (this._lock)
        {
            this._utcNow = this._utcNow.Add(duration);
            foreach (ManualTimer timer in this._timers.ToArray())
            {
                if (timer.CheckAndFire(this._utcNow))
                {
                    timersToFire.Add(timer);
                }
            }
        }

        foreach (ManualTimer timer in timersToFire)
        {
            timer.Fire();
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _provider;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private DateTimeOffset _dueTime;
        private TimeSpan _period;
        private bool _disposed;
        private readonly object _lock = new();

        public ManualTimer(
            ManualTimeProvider provider,
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            this._provider = provider;
            this._callback = callback;
            this._state = state;
            this._period = period;
            this._dueTime = dueTime == Timeout.InfiniteTimeSpan
                ? DateTimeOffset.MaxValue
                : provider._utcNow.Add(dueTime);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (this._lock)
            {
                if (this._disposed)
                {
                    return false;
                }

                this._period = period;
                this._dueTime = dueTime == Timeout.InfiniteTimeSpan
                    ? DateTimeOffset.MaxValue
                    : this._provider.GetUtcNow().Add(dueTime);
                return true;
            }
        }

        public bool CheckAndFire(DateTimeOffset currentTime)
        {
            lock (this._lock)
            {
                if (this._disposed)
                {
                    return false;
                }

                if (currentTime >= this._dueTime)
                {
                    if (this._period == Timeout.InfiniteTimeSpan)
                    {
                        this._dueTime = DateTimeOffset.MaxValue;
                    }
                    else
                    {
                        this._dueTime = currentTime.Add(this._period);
                    }
                    return true;
                }

                return false;
            }
        }

        public void Fire()
        {
            this._callback(this._state);
        }

        public void Dispose()
        {
            lock (this._lock)
            {
                this._disposed = true;
            }
            lock (this._provider._lock)
            {
                this._provider._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
