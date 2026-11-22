namespace DynamicIsland;

// Position advances from a monotonic anchor. UTC belongs only to external timeline reports.
internal sealed class PlaybackClock
{
    internal static readonly TimeSpan MaximumReportAge = TimeSpan.FromHours(24);
    internal static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan SeekProtection = TimeSpan.FromSeconds(5);
    // Reports that keep a Playing state, fresh timestamps and the same position for this long are a stall.
    internal static readonly TimeSpan StallThreshold = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan StillTolerance = TimeSpan.FromMilliseconds(250);
    const int EarliestReliableYear = 2000;
    readonly TimeProvider _time;
    TimeSpan _position;
    long _anchoredAt;
    bool _hasTimeline;
    TimeSpan _reportedPosition;
    TimeSpan? _reportedDuration;
    DateTimeOffset _reportedStamp, _lastReliableStamp;
    bool _seekFence;
    DateTimeOffset _seekUtc, _seekSourceStamp;
    long _seekAnchoredAt, _utcObservedAt;
    DateTimeOffset _lastObservedUtc;
    bool _utcDiscontinuity;
    bool _stallWatch, _stalled;
    long _stallStartedAt;

    public PlaybackClock(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _anchoredAt = _time.GetTimestamp();
        (_utcObservedAt, _lastObservedUtc) = (_anchoredAt, _time.GetUtcNow());
    }

    public bool HasPosition { get; private set; }
    public bool IsPlaying { get; private set; }
    public double Rate { get; private set; } = 1;
    public bool LastTimelineReliable { get; private set; }
    // True while the source says Playing but its position has not moved (buffering or a stuck track).
    public bool Stalled => _stalled;

    public TimeSpan Position
    {
        get
        {
            if (!HasPosition) return TimeSpan.Zero;
            TimeSpan elapsed = IsPlaying ? _time.GetElapsedTime(_anchoredAt) : TimeSpan.Zero;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            return Add(_position, elapsed, Rate);
        }
    }

    public void SetPlayback(bool playing, double rate)
    {
        rate = ValidRate(rate);
        if (playing == IsPlaying && rate == Rate) return;
        _position = Position;
        _anchoredAt = _time.GetTimestamp();
        (IsPlaying, Rate) = (playing, rate);
        _stallWatch = _stalled = false;
    }

    public void AnchorPosition(TimeSpan position)
    {
        _position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        _anchoredAt = _time.GetTimestamp();
        HasPosition = true;
    }

    public void ConfirmSeek(TimeSpan position)
    {
        AnchorPosition(position);
        _seekFence = true;
        _seekAnchoredAt = _time.GetTimestamp();
        _seekUtc = ObserveUtc();
        _seekSourceStamp = _lastReliableStamp;
    }

    public bool ApplyTimeline(TimeSpan position, DateTimeOffset updatedAt, TimeSpan? independentDuration = null)
    {
        if (_hasTimeline && updatedAt == _reportedStamp && position == _reportedPosition && independentDuration == _reportedDuration) return false;
        bool changedWithoutStamp = _hasTimeline && updatedAt == _reportedStamp && position != _reportedPosition;
        DateTimeOffset now = ObserveUtc();
        bool reliableStamp = updatedAt.Year >= EarliestReliableYear
            && (updatedAt <= now || updatedAt - now <= TimestampTolerance);
        if (!reliableStamp && updatedAt.Year >= EarliestReliableYear && _lastReliableStamp.Year >= EarliestReliableYear) return false;
        TimeSpan reportAge = reliableStamp && updatedAt < now ? now - updatedAt : TimeSpan.Zero;
        bool freshAfterUtcJump = _utcDiscontinuity && reliableStamp && reportAge <= TimestampTolerance;
        if (_utcDiscontinuity && !freshAfterUtcJump && updatedAt.Year >= EarliestReliableYear && _hasTimeline) return false;
        if (reliableStamp && _lastReliableStamp.Year >= EarliestReliableYear && updatedAt < _lastReliableStamp && !freshAfterUtcJump) return false;
        // A confirmed seek owns the anchor until a report newer than both the source and acknowledgement.
        // Reset clears this fence, so another track can reuse the very same source timestamp.
        if (_seekFence)
        {
            bool protectedWindow = _time.GetElapsedTime(_seekAnchoredAt) < SeekProtection;
            if (updatedAt.Year < EarliestReliableYear)
            {
                // Without timestamps, use a short receipt-time fence and target proximity, not an eternal lock.
                if (protectedWindow && Math.Abs((double)position.Ticks - Position.Ticks) > TimestampTolerance.Ticks) return false;
                if (!protectedWindow) _seekFence = false;
            }
            else
            {
                if (!reliableStamp || updatedAt == _seekSourceStamp
                    || protectedWindow && (updatedAt <= _seekUtc || updatedAt <= _seekSourceStamp || _utcDiscontinuity)
                    || !protectedWindow && updatedAt <= _seekSourceStamp && !freshAfterUtcJump) return false;
                _seekFence = false;
            }
        }

        LastTimelineReliable = reliableStamp && !changedWithoutStamp && reportAge <= MaximumReportAge
            && (independentDuration is not TimeSpan duration || duration <= TimeSpan.Zero || reportAge <= duration);
        UpdateStall(position, updatedAt, reliableStamp);
        TimeSpan age = IsPlaying && LastTimelineReliable && !_stalled ? reportAge : TimeSpan.Zero;
        AnchorPosition(Add(position < TimeSpan.Zero ? TimeSpan.Zero : position, age, Rate));
        _hasTimeline = true;
        (_reportedStamp, _reportedPosition) = (updatedAt, position);
        _reportedDuration = independentDuration;
        if (reliableStamp) _lastReliableStamp = updatedAt;
        if (freshAfterUtcJump) _utcDiscontinuity = false;
        return true;
    }

    // Fresh timestamps with an unchanged position are not progress. The clock holds its place
    // after StallThreshold and resumes from the first report that moves.
    void UpdateStall(TimeSpan position, DateTimeOffset updatedAt, bool reliableStamp)
    {
        if (!IsPlaying || !_hasTimeline) { _stallWatch = _stalled = false; return; }
        if (!reliableStamp || updatedAt <= _reportedStamp) return;
        if ((position - _reportedPosition).Duration() >= StillTolerance)
        {
            _stallWatch = _stalled = false;
            return;
        }
        long now = _time.GetTimestamp();
        if (!_stallWatch) { _stallWatch = true; _stallStartedAt = now; }
        else if (_time.GetElapsedTime(_stallStartedAt, now) >= StallThreshold) _stalled = true;
    }

    public void Reset()
    {
        _position = TimeSpan.Zero;
        _anchoredAt = _time.GetTimestamp();
        HasPosition = _hasTimeline = _seekFence = false;
        _stallWatch = _stalled = false;
        _reportedPosition = TimeSpan.Zero;
        _reportedDuration = null;
        _reportedStamp = _lastReliableStamp = _seekUtc = _seekSourceStamp = default;
        LastTimelineReliable = false;
        _utcDiscontinuity = false;
        (_utcObservedAt, _lastObservedUtc) = (_anchoredAt, _time.GetUtcNow());
    }

    internal static double ValidRate(double rate) => double.IsFinite(rate) && rate > 0 ? rate : 1;

    DateTimeOffset ObserveUtc()
    {
        DateTimeOffset now = _time.GetUtcNow();
        long timestamp = _time.GetTimestamp();
        double divergence = (double)(now - _lastObservedUtc).Ticks - _time.GetElapsedTime(_utcObservedAt, timestamp).Ticks;
        if (Math.Abs(divergence) > TimestampTolerance.Ticks) _utcDiscontinuity = true;
        (_utcObservedAt, _lastObservedUtc) = (timestamp, now);
        return now;
    }

    static TimeSpan Add(TimeSpan position, TimeSpan elapsed, double rate)
    {
        double ticks = position.Ticks + elapsed.Ticks * rate;
        if (ticks >= TimeSpan.MaxValue.Ticks) return TimeSpan.MaxValue;
        if (ticks <= 0 || double.IsNaN(ticks)) return TimeSpan.Zero;
        return TimeSpan.FromTicks((long)ticks);
    }
}
