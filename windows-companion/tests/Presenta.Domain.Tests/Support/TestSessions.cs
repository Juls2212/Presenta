namespace Presenta.Domain.Tests.Support;

internal static class TestSessions
{
    /// <summary>Options with a pace tolerance so wide that pace alerts never fire. Used to isolate time alerts.</summary>
    public static readonly SessionOptions NoPaceAlerts = new() { PaceToleranceFraction = 10 };

    public static PresentationSession Configured(
        FakeTimeProvider clock,
        int slides = 20,
        double minutes = 20,
        SessionOptions? options = null,
        string? name = null)
    {
        var config = SessionConfiguration.Create(slides, TimeSpan.FromMinutes(minutes), name);
        Assert.True(config.Succeeded);
        return new PresentationSession(config.Value!, clock, options);
    }

    public static PresentationSession Started(
        FakeTimeProvider clock,
        int slides = 20,
        double minutes = 20,
        SessionOptions? options = null,
        string? name = null)
    {
        var session = Configured(clock, slides, minutes, options, name);
        Assert.True(session.Start().Succeeded);
        return session;
    }
}
