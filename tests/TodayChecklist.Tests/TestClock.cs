using TodayChecklist.Services;

namespace TodayChecklist.Tests;

internal sealed class TestClock : IClock
{
    public TestClock(DateTimeOffset now)
    {
        Set(now);
    }

    public DateTimeOffset UtcNow { get; private set; }

    public DateOnly Today { get; private set; }

    public void Set(DateTimeOffset now)
    {
        UtcNow = now.ToUniversalTime();
        Today = DateOnly.FromDateTime(now.LocalDateTime);
    }
}

