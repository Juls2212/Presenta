using Presenta.Ai;

namespace Presenta.Ai.Tests;

public class SanitizerTests
{
    [Theory]
    [InlineData("Speed up a little", "Speed up a little")]
    [InlineData("  Speed up a little  ", "Speed up a little")]
    [InlineData("\"Speed up a little\"", "Speed up a little")]
    [InlineData("'Speed up a little'", "Speed up a little")]
    [InlineData("“Speed up a little”", "Speed up a little")]
    [InlineData("«Speed up a little»", "Speed up a little")]
    [InlineData("**Speed up** a little", "Speed up a little")]
    [InlineData("# Speed up a little", "Speed up a little")]
    [InlineData("- Speed up a little", "Speed up a little")]
    [InlineData("> Speed up a little", "Speed up a little")]
    [InlineData("1. Speed up a little", "Speed up a little")]
    [InlineData("`Speed up a little`", "Speed up a little")]
    [InlineData("__Speed up__ a little", "Speed up a little")]
    [InlineData("Speed up\na little", "Speed up a little")]
    [InlineData("Speed up\r\n\r\na   little", "Speed up a little")]
    [InlineData("Speed\tup a little", "Speed up a little")]
    [InlineData("\u00A1Go faster now, please!", "\u00A1Go faster now, please!")]
    public void Good_text_is_cleaned_to_one_plain_line(string raw, string expected)
    {
        Assert.Equal(expected, AiTextSanitizer.Sanitize(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    [InlineData("\"\"")]
    [InlineData("****")]
    [InlineData("Visit https://example.com now")]
    [InlineData("see http://example.com")]
    [InlineData("go to www.example.org")]
    [InlineData("open example.com today")]
    [InlineData("ftp://files.example")]
    [InlineData("Speed up\u0000 a little")]
    [InlineData("Speed up\u0007 a little")]
    [InlineData("Speed up \u001B[31m a little")]
    [InlineData("Speed up a little 😀")]
    [InlineData("Speed up a little ❤️")]
    [InlineData("Speed up​ a little")]
    public void Unusable_text_is_rejected(string? raw)
    {
        Assert.Null(AiTextSanitizer.Sanitize(raw));
    }

    [Fact]
    public void A_text_of_exactly_the_limit_is_kept_and_one_over_is_cut_at_a_word()
    {
        var exact = new string('a', 10) + " " + new string('b', 10) + " " + new string('c', 10) + " " + new string('d', 7); // 40
        Assert.Equal(AiLimits.MaxTextLength, exact.Length);
        Assert.Equal(exact, AiTextSanitizer.Sanitize(exact));

        var over = "Slow down a little because you are going fast";
        var cut = AiTextSanitizer.Sanitize(over);
        Assert.NotNull(cut);
        Assert.True(cut!.Length <= AiLimits.MaxTextLength);
        Assert.StartsWith("Slow down a little", cut, StringComparison.Ordinal);
        Assert.True(over.StartsWith(cut, StringComparison.Ordinal), "a cut keeps whole words from the start");
    }

    [Fact]
    public void A_cut_that_would_leave_too_few_words_is_rejected()
    {
        Assert.Null(AiTextSanitizer.Sanitize("Extraordinarily-long-single-word-without-any-spaces-at-all"));
        Assert.Null(AiTextSanitizer.Sanitize("Supercalifragilisticexpialidocious and more"));
        Assert.Null(AiTextSanitizer.Sanitize(new string('x', 200)));
    }

    [Fact]
    public void The_result_never_exceeds_the_limit_whatever_the_input()
    {
        var random = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var words = Enumerable.Range(0, random.Next(1, 30)).Select(_ => new string('a', random.Next(1, 12)));
            var result = AiTextSanitizer.Sanitize(string.Join(' ', words));
            Assert.True(result is null || result.Length <= AiLimits.MaxTextLength);
            Assert.True(result is null || !result.Contains('\n'));
        }
    }
}

public class PromptTests
{
    private static AiSituation Situation() =>
        AiSituation.Create("SpeedUpSlightly", "TimeRunningOut", "Behind", slidesRemaining: 9, secondsRemaining: 300, plannedSlides: 20, plannedSeconds: 900);

    [Fact]
    public void The_system_prompt_asks_for_one_short_spanish_sentence_and_names_the_limit()
    {
        var system = AiPromptBuilder.SystemPrompt;

        Assert.Contains("Spanish (Colombia)", system, StringComparison.Ordinal);
        Assert.Contains("one sentence", system, StringComparison.Ordinal);
        Assert.Contains("at most 40 characters", system, StringComparison.Ordinal);
        Assert.Contains("no emojis", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no quotes", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no markdown", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no line breaks", system, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(40, AiLimits.MaxTextLength);
    }

    [Fact]
    public void The_user_message_is_pinned_and_holds_only_codes_and_coarse_numbers()
    {
        var prompt = AiPromptBuilder.Build(Situation());

        Assert.Equal(
            "Situation: recommendation=SpeedUpSlightly; alert=TimeRunningOut; pace=Behind; slidesRemaining=9; minutesRemaining=5; plannedSlides=20; plannedMinutes=15. Write the tip.",
            prompt.User);
        Assert.Equal(AiPromptBuilder.SystemPrompt, prompt.System);
    }

    [Fact]
    public void An_idle_alert_is_written_as_none()
    {
        var situation = AiSituation.Create("OnTrack", null, "OnTrack", 3, 120, 10, 600);

        Assert.Contains("alert=none;", AiPromptBuilder.Build(situation).User, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 7)]
    [InlineData(10, 10)]
    [InlineData(11, 10)]
    [InlineData(13, 15)]
    [InlineData(28, 30)]
    [InlineData(30, 30)]
    [InlineData(31, 30)]
    [InlineData(34, 30)]
    [InlineData(36, 40)]
    [InlineData(-3, -3)]
    [InlineData(-13, -15)]
    [InlineData(-95, -100)]
    public void Numbers_are_rounded_to_coarse_buckets_keeping_the_sign(int value, int expected)
    {
        Assert.Equal(expected, AiPromptBuilder.Coarse(value));
    }

    [Fact]
    public void Nearby_situations_share_a_key_and_different_ones_do_not()
    {
        var a = AiSituation.Create("SpeedUpNow", "BehindSchedule", "Behind", 31, 1_800, 40, 2_400);
        var b = AiSituation.Create("SpeedUpNow", "BehindSchedule", "Behind", 34, 1_790, 40, 2_400);
        var other = AiSituation.Create("SlowDown", "TooFast", "TooFast", 31, 1_800, 40, 2_400);

        Assert.Equal(a.Key, b.Key);
        Assert.NotEqual(a.Key, other.Key);
    }

    [Fact]
    public void The_exact_request_body_is_pinned()
    {
        var body = OpenAiCompatibleClient.BuildRequestJson("test-model", AiPromptBuilder.Build(Situation()), new AiRequestShape(AiTokenLimitParameter.MaxCompletionTokens, 400, null, null));

        const string expected = "{\"model\":\"test-model\",\"messages\":[{\"role\":\"system\",\"content\":\"You are a concise presentation coach. Reply in Spanish (Colombia) with exactly one sentence of at most 40 characters. No emojis, no quotes, no markdown, no line breaks. Reply with the tip only.\"},{\"role\":\"user\",\"content\":\"Situation: recommendation=SpeedUpSlightly; alert=TimeRunningOut; pace=Behind; slidesRemaining=9; minutesRemaining=5; plannedSlides=20; plannedMinutes=15. Write the tip.\"}],\"max_completion_tokens\":400}";
        Assert.Equal(expected, body);
    }

    [Fact]
    public async Task The_canned_client_answers_deterministically_without_a_network()
    {
        var client = new CannedAiTextClient();

        var first = await client.GenerateAsync(AiPromptBuilder.Build(Situation()), CancellationToken.None);
        var second = await client.GenerateAsync(AiPromptBuilder.Build(Situation()), CancellationToken.None);

        Assert.Equal("Fake AI tip: SpeedUpSlightly", first.Text);
        Assert.Equal(first.Text, second.Text);
        Assert.NotNull(AiTextSanitizer.Sanitize(first.Text));
        Assert.Equal(2, client.Calls);
    }
}
