using System.Globalization;

namespace Presenta.Ai;

/// <summary>
/// The aggregate facts a tip is based on, already rounded to coarse values. This is everything that ever leaves the
/// computer: codes and counts, never a name, a title, a file or a machine (ADR-74). The same situation always gives the
/// same <see cref="Key"/> and the same prompt.
/// </summary>
public sealed record AiSituation(
    string Recommendation,
    string? Alert,
    string Pace,
    int SlidesRemaining,
    int MinutesRemaining,
    int PlannedSlides,
    int PlannedMinutes)
{
    /// <summary>Builds a situation from exact values, keeping only coarse ones.</summary>
    public static AiSituation Create(
        string recommendation,
        string? alert,
        string pace,
        int slidesRemaining,
        long secondsRemaining,
        int plannedSlides,
        long plannedSeconds) =>
        new(
            recommendation,
            alert,
            pace,
            AiPromptBuilder.Coarse(slidesRemaining),
            AiPromptBuilder.Coarse(MinutesFromSeconds(secondsRemaining)),
            AiPromptBuilder.Coarse(plannedSlides),
            AiPromptBuilder.Coarse(MinutesFromSeconds(plannedSeconds)));

    /// <summary>Cache key: same coarse situation, same key.</summary>
    public string Key => string.Create(
        CultureInfo.InvariantCulture,
        $"{Recommendation}|{Alert ?? "none"}|{Pace}|{SlidesRemaining}|{MinutesRemaining}|{PlannedSlides}|{PlannedMinutes}");

    private static int MinutesFromSeconds(long seconds) => (int)Math.Clamp((long)Math.Round(seconds / 60.0, MidpointRounding.AwayFromZero), -9999, 9999);
}

/// <summary>Builds the two messages sent to the model.</summary>
public static class AiPromptBuilder
{
    /// <summary>The instruction to the model (English text asking for a Spanish answer). The limit comes from <see cref="AiLimits"/>.</summary>
    public static readonly string SystemPrompt = string.Create(
        CultureInfo.InvariantCulture,
        $"You are a concise presentation coach. Reply in Spanish (Colombia) with exactly one sentence of at most {AiLimits.MaxTextLength} characters. No emojis, no quotes, no markdown, no line breaks. Reply with the tip only.");

    public static AiPrompt Build(AiSituation situation) => new(
        SystemPrompt,
        string.Create(
            CultureInfo.InvariantCulture,
            $"Situation: recommendation={situation.Recommendation}; alert={situation.Alert ?? "none"}; pace={situation.Pace}; slidesRemaining={situation.SlidesRemaining}; minutesRemaining={situation.MinutesRemaining}; plannedSlides={situation.PlannedSlides}; plannedMinutes={situation.PlannedMinutes}. Write the tip."));

    /// <summary>
    /// Rounds a count so that nearby situations share a key: exact up to 10, then to the nearest 5 up to 30, then to the
    /// nearest 10. The sign is kept (a negative value is time over the plan).
    /// </summary>
    public static int Coarse(int value)
    {
        var magnitude = Math.Abs(value);
        var rounded = magnitude <= 10
            ? magnitude
            : magnitude <= 30
                ? (int)(Math.Round(magnitude / 5.0, MidpointRounding.AwayFromZero) * 5)
                : (int)(Math.Round(magnitude / 10.0, MidpointRounding.AwayFromZero) * 10);
        return value < 0 ? -rounded : rounded;
    }
}

/// <summary>A deterministic stand-in for the provider: never touches the network. For the developer host and tests.</summary>
public sealed class CannedAiTextClient : IAiTextClient
{
    private const string Marker = "recommendation=";

    public int Calls { get; private set; }

    public Task<AiTextResult> GenerateAsync(AiPrompt prompt, CancellationToken cancellationToken)
    {
        Calls++;
        var start = prompt.User.IndexOf(Marker, StringComparison.Ordinal);
        var code = "unknown";
        if (start >= 0)
        {
            var from = start + Marker.Length;
            var end = prompt.User.IndexOf(';', from);
            code = end > from ? prompt.User[from..end] : code;
        }

        return Task.FromResult(AiTextResult.Success("Fake AI tip: " + code));
    }
}
