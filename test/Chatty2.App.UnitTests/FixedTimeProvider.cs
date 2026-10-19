namespace Chatty2.App.UnitTests;

/// <summary>A clock stuck at one instant, in a pinned time zone (UTC unless told otherwise).</summary>
public sealed class FixedTimeProvider(DateTimeOffset now, TimeZoneInfo? localTimeZone = null) : TimeProvider
{
    // Pinning LocalTimeZone alongside the fixed instant keeps GetLocalNow() deterministic
    // regardless of the machine running the tests.
    public override TimeZoneInfo LocalTimeZone => localTimeZone ?? TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => now;
}
