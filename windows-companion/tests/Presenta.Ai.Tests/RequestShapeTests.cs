using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Presenta.Server;

namespace Presenta.Ai.Tests;

/// <summary>The request shape for current OpenAI chat models, its overrides and the adaptive retry (ADR-80).</summary>
public class RequestShapeTests
{
    private const string Key = "test-key-0123456789";

    private static readonly AiRequestShape DefaultShape = new(AiTokenLimitParameter.MaxCompletionTokens, AiDefaults.MaxTokens, null, null);

    private static AiPrompt Prompt() =>
        AiPromptBuilder.Build(AiSituation.Create("SpeedUpSlightly", "TimeRunningOut", "Behind", 9, 300, 20, 900));

    private static HttpClient NewHttp() => new(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };

    private static OpenAiCompatibleClient ClientFor(LocalAiServer server, Func<AiEndpointOptions>? make = null, TimeSpan? timeout = null)
    {
        var options = make?.Invoke() ?? new AiEndpointOptions(new Uri(server.BaseUrl), "test-model", Key);
        return new OpenAiCompatibleClient(NewHttp(), timeout is null ? options : Clone(options, timeout.Value));
    }

    private static AiEndpointOptions Clone(AiEndpointOptions o, TimeSpan timeout) => new(o.BaseUrl, o.Model, Key)
    {
        Timeout = timeout,
        MaxTokens = o.MaxTokens,
        TokenLimitParameter = o.TokenLimitParameter,
        Temperature = o.Temperature,
        ReasoningEffort = o.ReasoningEffort,
    };

    private static Func<AiEndpointOptions> Options(
        LocalAiServer server,
        AiTokenLimitParameter parameter = AiTokenLimitParameter.MaxCompletionTokens,
        double? temperature = null,
        string? effort = null) =>
        () => new AiEndpointOptions(new Uri(server.BaseUrl), "test-model", Key)
        {
            TokenLimitParameter = parameter,
            Temperature = temperature,
            ReasoningEffort = effort,
        };

    /// <summary>A provider that rejects a request carrying any of <paramref name="rejected"/> with the usual 400 error.</summary>
    private static Func<HttpContext, SeenRequest, Task> Strict(params (string Field, string Message)[] rejected) =>
        (context, request) =>
        {
            using var body = JsonDocument.Parse(request.Body);
            foreach (var (field, message) in rejected)
            {
                if (body.RootElement.TryGetProperty(field, out _))
                {
                    return LocalAiServer.Reply(context, 400, "{\"error\":{\"message\":" + JsonSerializer.Serialize(message) + ",\"type\":\"invalid_request_error\",\"param\":\"" + field + "\",\"code\":null}}");
                }
            }

            return LocalAiServer.Choice(context, "Speed up a little");
        };

    private static HashSet<string> FieldsOf(SeenRequest request)
    {
        using var body = JsonDocument.Parse(request.Body);
        return body.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }

    private const string MaxTokensRejected = "Unsupported parameter: 'max_tokens' is not supported with this model. Use 'max_completion_tokens' instead.";
    private const string MaxCompletionRejected = "Unrecognized request argument supplied: max_completion_tokens";
    private const string TemperatureRejected = "Unsupported value: 'temperature' does not support 0.4 with this model. Only the default (1) value is supported.";
    private const string EffortRejected = "Unrecognized request argument supplied: reasoning_effort";

    // ---- default body ----

    [Fact]
    public async Task The_default_body_has_the_completion_token_limit_and_no_temperature_or_effort()
    {
        await using var server = await LocalAiServer.StartAsync((context, _) => LocalAiServer.Choice(context, "Speed up a little"));

        var answer = await ClientFor(server).GenerateAsync(Prompt(), CancellationToken.None);
        Assert.Equal(1, answer.Attempts);

        var fields = FieldsOf(Assert.Single(server.Seen));
        Assert.Equal(new[] { "max_completion_tokens", "messages", "model" }, fields.OrderBy(f => f, StringComparer.Ordinal));
        using var body = JsonDocument.Parse(server.Seen[0].Body);
        Assert.Equal(400, body.RootElement.GetProperty("max_completion_tokens").GetInt32());
    }

    [Fact]
    public void The_golden_body_for_the_defaults_is_the_pinned_text_and_carries_only_codes_and_aggregate_numbers()
    {
        var body = OpenAiCompatibleClient.BuildRequestJson("test-model", Prompt(), DefaultShape);

        const string expected = "{\"model\":\"test-model\",\"messages\":[{\"role\":\"system\",\"content\":\"You are a concise presentation coach. Reply in Spanish (Colombia) with exactly one sentence of at most 40 characters. No emojis, no quotes, no markdown, no line breaks. Reply with the tip only.\"},{\"role\":\"user\",\"content\":\"Situation: recommendation=SpeedUpSlightly; alert=TimeRunningOut; pace=Behind; slidesRemaining=9; minutesRemaining=5; plannedSlides=20; plannedMinutes=15. Write the tip.\"}],\"max_completion_tokens\":400}";
        Assert.Equal(expected, body);
        foreach (var canary in new[] { "Quarterly", "secret", ".pptx", "PowerPoint", "POWERPNT", Environment.MachineName, Environment.UserName }.Where(c => c.Length >= 3))
        {
            Assert.DoesNotContain(canary, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Every_optional_field_is_written_when_set_and_the_other_token_name_is_never_sent_with_it()
    {
        var all = new AiRequestShape(AiTokenLimitParameter.MaxTokens, 64, 0.3, "low");

        var body = OpenAiCompatibleClient.BuildRequestJson("m", Prompt(), all);

        Assert.EndsWith("\"max_tokens\":64,\"temperature\":0.3,\"reasoning_effort\":\"low\"}", body, StringComparison.Ordinal);
        Assert.DoesNotContain("max_completion_tokens", body, StringComparison.Ordinal);
    }

    // ---- environment overrides ----

    private static AiEndpointOptions Read(Dictionary<string, string?> extra)
    {
        var values = new Dictionary<string, string?>
        {
            [AiConfiguration.BaseUrlVariable] = "https://api.example.com/v1",
            [AiConfiguration.ModelVariable] = "some-model",
            [AiConfiguration.ApiKeyVariable] = "key-placeholder",
        };
        foreach (var pair in extra)
        {
            values[pair.Key] = pair.Value;
        }

        var result = AiConfiguration.FromEnvironment(name => values.GetValueOrDefault(name));
        Assert.True(result.IsAvailable);
        return result.Options!;
    }

    [Fact]
    public void Without_the_optional_variables_the_documented_defaults_apply()
    {
        var options = Read(new());

        Assert.Equal(AiTokenLimitParameter.MaxCompletionTokens, options.TokenLimitParameter);
        Assert.Equal(400, options.MaxTokens);
        Assert.Null(options.Temperature);
        Assert.Null(options.ReasoningEffort);
        Assert.Equal(TimeSpan.FromSeconds(8), options.Timeout);
    }

    [Theory]
    [InlineData("max_tokens", AiTokenLimitParameter.MaxTokens)]
    [InlineData("MAX_TOKENS", AiTokenLimitParameter.MaxTokens)]
    [InlineData(" max_tokens ", AiTokenLimitParameter.MaxTokens)]
    [InlineData("max_completion_tokens", AiTokenLimitParameter.MaxCompletionTokens)]
    [InlineData("nonsense", AiTokenLimitParameter.MaxCompletionTokens)]
    [InlineData("", AiTokenLimitParameter.MaxCompletionTokens)]
    public void The_token_parameter_variable_chooses_the_field_name(string value, AiTokenLimitParameter expected)
    {
        Assert.Equal(expected, Read(new() { [AiConfiguration.TokenParameterVariable] = value }).TokenLimitParameter);
    }

    [Theory]
    [InlineData("64", 64)]
    [InlineData("16", 16)]
    [InlineData("8000", 8000)]
    [InlineData("15", 400)]
    [InlineData("8001", 400)]
    [InlineData("-5", 400)]
    [InlineData("abc", 400)]
    [InlineData("1e3", 400)]
    [InlineData("", 400)]
    public void The_token_limit_variable_is_read_within_its_range_or_ignored(string value, int expected)
    {
        Assert.Equal(expected, Read(new() { [AiConfiguration.MaxTokensVariable] = value }).MaxTokens);
    }

    [Theory]
    [InlineData("0", 0.0)]
    [InlineData("0.2", 0.2)]
    [InlineData("1", 1.0)]
    [InlineData("2", 2.0)]
    [InlineData(" 0.7 ", 0.7)]
    [InlineData("2.1", null)]
    [InlineData("-0.1", null)]
    [InlineData("hot", null)]
    [InlineData("", null)]
    public void The_temperature_variable_turns_the_field_on_only_with_a_valid_number(string value, double? expected)
    {
        Assert.Equal(expected, Read(new() { [AiConfiguration.TemperatureVariable] = value }).Temperature);
    }

    [Theory]
    [InlineData("low", "low")]
    [InlineData("MINIMAL", "minimal")]
    [InlineData(" high ", "high")]
    [InlineData("xhigh", "xhigh")]
    [InlineData("with space", null)]
    [InlineData("quo\"te", null)]
    [InlineData("a-very-very-very-very-long-value", null)]
    [InlineData("", null)]
    public void The_reasoning_effort_variable_turns_the_field_on_only_with_a_plain_word(string value, string? expected)
    {
        Assert.Equal(expected, Read(new() { [AiConfiguration.ReasoningEffortVariable] = value }).ReasoningEffort);
    }

    [Theory]
    [InlineData("12", 12)]
    [InlineData("1", 1)]
    [InlineData("30", 30)]
    [InlineData("0", 8)]
    [InlineData("31", 8)]
    [InlineData("abc", 8)]
    [InlineData("", 8)]
    public void The_timeout_variable_is_read_in_seconds_within_its_range_or_ignored(string value, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), Read(new() { [AiConfiguration.TimeoutVariable] = value }).Timeout);
    }

    [Fact]
    public async Task The_overrides_reach_the_wire()
    {
        await using var server = await LocalAiServer.StartAsync((context, _) => LocalAiServer.Choice(context, "Speed up a little"));
        var options = Read(new()
        {
            [AiConfiguration.TokenParameterVariable] = "max_tokens",
            [AiConfiguration.MaxTokensVariable] = "64",
            [AiConfiguration.TemperatureVariable] = "0.5",
            [AiConfiguration.ReasoningEffortVariable] = "low",
        });
        var client = new OpenAiCompatibleClient(NewHttp(), new AiEndpointOptions(new Uri(server.BaseUrl), options.Model, Key)
        {
            TokenLimitParameter = options.TokenLimitParameter,
            MaxTokens = options.MaxTokens,
            Temperature = options.Temperature,
            ReasoningEffort = options.ReasoningEffort,
        });

        await client.GenerateAsync(Prompt(), CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(server.Seen).Body);
        Assert.Equal(64, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.5, body.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal("low", body.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.False(body.RootElement.TryGetProperty("max_completion_tokens", out _));
    }

    [Fact]
    public void The_options_still_hide_the_key_with_the_new_settings()
    {
        var options = Read(new() { [AiConfiguration.TemperatureVariable] = "0.5" });

        Assert.DoesNotContain("key-placeholder", options.ToString(), StringComparison.Ordinal);
    }

    // ---- adaptive retry ----

    [Fact]
    public async Task A_rejected_completion_token_name_is_retried_once_with_max_tokens()
    {
        await using var server = await LocalAiServer.StartAsync(Strict(("max_completion_tokens", MaxCompletionRejected)));
        var client = ClientFor(server);

        var result = await client.GenerateAsync(Prompt(), CancellationToken.None);

        Assert.Equal("Speed up a little", result.Text);
        Assert.Equal(2, server.Seen.Count);
        Assert.Contains("max_completion_tokens", FieldsOf(server.Seen[0]));
        Assert.Contains("max_tokens", FieldsOf(server.Seen[1]));
        Assert.DoesNotContain("max_completion_tokens", FieldsOf(server.Seen[1]));
        Assert.Equal(AiTokenLimitParameter.MaxTokens, client.CurrentShape.TokenParameter);
    }

    [Fact]
    public async Task A_rejected_max_tokens_is_retried_once_with_max_completion_tokens_even_though_the_message_names_both()
    {
        await using var server = await LocalAiServer.StartAsync(Strict(("max_tokens", MaxTokensRejected)));
        var client = ClientFor(server, Options(server, AiTokenLimitParameter.MaxTokens));

        var result = await client.GenerateAsync(Prompt(), CancellationToken.None);

        Assert.Equal("Speed up a little", result.Text);
        Assert.Equal(2, server.Seen.Count);
        Assert.Contains("max_completion_tokens", FieldsOf(server.Seen[1]));
        Assert.Equal(AiTokenLimitParameter.MaxCompletionTokens, client.CurrentShape.TokenParameter);
    }

    [Fact]
    public async Task A_rejected_temperature_is_dropped_on_the_retry()
    {
        await using var server = await LocalAiServer.StartAsync(Strict(("temperature", TemperatureRejected)));
        var client = ClientFor(server, Options(server, temperature: 0.4));

        var result = await client.GenerateAsync(Prompt(), CancellationToken.None);

        Assert.Equal("Speed up a little", result.Text);
        Assert.Equal(2, server.Seen.Count);
        Assert.Contains("temperature", FieldsOf(server.Seen[0]));
        Assert.DoesNotContain("temperature", FieldsOf(server.Seen[1]));
        Assert.Contains("max_completion_tokens", FieldsOf(server.Seen[1])); // nothing else changed
        Assert.Null(client.CurrentShape.Temperature);
    }

    [Fact]
    public async Task A_rejected_reasoning_effort_is_dropped_on_the_retry()
    {
        await using var server = await LocalAiServer.StartAsync(Strict(("reasoning_effort", EffortRejected)));
        var client = ClientFor(server, Options(server, effort: "low"));

        var result = await client.GenerateAsync(Prompt(), CancellationToken.None);

        Assert.Equal("Speed up a little", result.Text);
        Assert.Equal(2, server.Seen.Count);
        Assert.Equal(2, result.Attempts); // reported for the timing record only
        Assert.DoesNotContain("reasoning_effort", FieldsOf(server.Seen[1]));
        Assert.Null(client.CurrentShape.ReasoningEffort);
    }

    [Fact]
    public async Task One_message_naming_several_parameters_adjusts_all_of_them_in_the_single_retry()
    {
        await using var server = await LocalAiServer.StartAsync((context, request) =>
        {
            var fields = FieldsOf(request);
            return fields.Contains("temperature") || fields.Contains("reasoning_effort")
                ? LocalAiServer.Reply(context, 400, "{\"error\":{\"message\":\"Unsupported parameters: temperature, reasoning_effort\"}}")
                : LocalAiServer.Choice(context, "Speed up a little");
        });
        var client = ClientFor(server, Options(server, temperature: 0.4, effort: "low"));

        var result = await client.GenerateAsync(Prompt(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, server.Seen.Count);
        Assert.Equal(new AiRequestShape(AiTokenLimitParameter.MaxCompletionTokens, AiDefaults.MaxTokens, null, null), client.CurrentShape);
    }

    [Fact]
    public async Task The_working_shape_is_remembered_so_later_requests_need_no_retry()
    {
        await using var server = await LocalAiServer.StartAsync(Strict(("temperature", TemperatureRejected)));
        var client = ClientFor(server, Options(server, temperature: 0.4));

        await client.GenerateAsync(Prompt(), CancellationToken.None);
        await client.GenerateAsync(Prompt(), CancellationToken.None);
        await client.GenerateAsync(Prompt(), CancellationToken.None);

        Assert.Equal(4, server.Seen.Count); // 2 for the first answer, then 1 each
        Assert.All(server.Seen.Skip(1), request => Assert.DoesNotContain("temperature", FieldsOf(request)));
    }

    [Fact]
    public async Task A_shape_that_still_fails_is_not_remembered_and_there_is_never_a_third_request()
    {
        await using var server = await LocalAiServer.StartAsync(Strict(
            ("max_completion_tokens", MaxCompletionRejected),
            ("max_tokens", MaxTokensRejected)));
        var client = ClientFor(server);

        var result = await client.GenerateAsync(Prompt(), CancellationToken.None);

        Assert.Equal(AiFailureKind.Rejected, result.Failure);
        Assert.Equal(2, server.Seen.Count);
        Assert.Equal(AiTokenLimitParameter.MaxCompletionTokens, client.CurrentShape.TokenParameter);
    }

    [Theory]
    [InlineData("Invalid 'messages': expected an array.")]
    [InlineData("'max_completion_tokens' is too large: 99999. This model supports at most 4096 completion tokens.")]
    [InlineData("Unsupported parameter: 'top_p' is not supported with this model.")]
    [InlineData("Rate of requests too high, please slow down")]
    [InlineData("")]
    public async Task A_400_that_does_not_name_a_sent_parameter_as_unsupported_is_not_retried(string message)
    {
        await using var server = await LocalAiServer.StartAsync((context, _) =>
            LocalAiServer.Reply(context, 400, "{\"error\":{\"message\":" + JsonSerializer.Serialize(message) + "}}"));
        var client = ClientFor(server, Options(server, temperature: 0.4));

        var result = await client.GenerateAsync(Prompt(), CancellationToken.None);

        Assert.Equal(AiFailureKind.Rejected, result.Failure);
        Assert.Single(server.Seen);
        Assert.Equal(0.4, client.CurrentShape.Temperature);
    }

    [Fact]
    public async Task A_400_that_is_not_json_is_not_retried()
    {
        await using var server = await LocalAiServer.StartAsync((context, _) => LocalAiServer.Reply(context, 400, "<html>bad</html>", "text/html"));

        var result = await ClientFor(server).GenerateAsync(Prompt(), CancellationToken.None);

        Assert.Equal(AiFailureKind.Rejected, result.Failure);
        Assert.Single(server.Seen);
    }

    [Theory]
    [InlineData(401, AiFailureKind.Unauthorized)]
    [InlineData(429, AiFailureKind.RateLimited)]
    [InlineData(500, AiFailureKind.ServerError)]
    public async Task Other_statuses_on_the_retry_are_reported_as_usual_and_the_shape_is_not_remembered(int status, AiFailureKind expected)
    {
        var calls = 0;
        await using var server = await LocalAiServer.StartAsync((context, _) =>
            ++calls == 1
                ? LocalAiServer.Reply(context, 400, "{\"error\":{\"message\":\"" + TemperatureRejected + "\"}}")
                : LocalAiServer.Reply(context, status, "{}"));
        var client = ClientFor(server, Options(server, temperature: 0.4));

        var result = await client.GenerateAsync(Prompt(), CancellationToken.None);

        Assert.Equal(expected, result.Failure);
        Assert.Equal(0.4, client.CurrentShape.Temperature);
    }

    [Fact]
    public async Task A_retry_that_answers_with_no_text_still_proves_the_shape_and_is_reported_as_no_text()
    {
        var calls = 0;
        await using var server = await LocalAiServer.StartAsync((context, _) =>
            ++calls == 1
                ? LocalAiServer.Reply(context, 400, "{\"error\":{\"message\":\"" + TemperatureRejected + "\"}}")
                : LocalAiServer.Reply(context, 200, "{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"\"}}]}"));
        var client = ClientFor(server, Options(server, temperature: 0.4));

        var result = await client.GenerateAsync(Prompt(), CancellationToken.None);

        Assert.Equal(AiFailureKind.NoText, result.Failure);
        Assert.Null(client.CurrentShape.Temperature);
    }

    // ---- limiter and breaker see one attempt ----

    [Fact]
    public async Task A_retried_answer_counts_as_one_attempt_for_the_limiter_and_the_breaker()
    {
        await using var server = await LocalAiServer.StartAsync(Strict(("temperature", TemperatureRejected)));
        var ai = new AiRecommendationEnricher(
            ClientFor(server, Options(server, temperature: 0.4)),
            AiAvailability.Available,
            new AiEnrichmentOptions { MinInterval = TimeSpan.Zero },
            TimeProvider.System) { Enabled = true };

        ai.Apply(Alerting());
        await ai.WhenIdleAsync();

        Assert.Equal(2, server.Seen.Count);
        Assert.Equal(1, ai.CallsThisSession);
        Assert.Equal(AiStatus.Ready, ai.Status);
        Assert.Equal("Speed up a little", ai.Apply(Alerting())!.FreeText);
    }

    [Fact]
    public async Task Empty_answers_through_the_real_client_show_the_no_text_status_and_leave_the_deterministic_wording()
    {
        await using var server = await LocalAiServer.StartAsync((context, _) =>
            LocalAiServer.Reply(context, 200, "{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"role\":\"assistant\",\"content\":\"\"}}]}"));
        var ai = new AiRecommendationEnricher(ClientFor(server), AiAvailability.Available, new AiEnrichmentOptions { MinInterval = TimeSpan.Zero }, TimeProvider.System) { Enabled = true };

        ai.Apply(Alerting());
        await ai.WhenIdleAsync();
        ai.Apply(Alerting());
        await ai.WhenIdleAsync();

        Assert.Equal(AiStatus.NoText, ai.Status);
        Assert.Null(ai.Apply(Alerting())!.FreeText);
        Assert.Single(server.Seen); // the same situation is not asked again
    }

    // ---- timeouts ----

    [Fact]
    public async Task The_time_budget_covers_the_retry_too()
    {
        var calls = 0;
        await using var server = await LocalAiServer.StartAsync(async (context, _) =>
        {
            if (++calls == 1)
            {
                await LocalAiServer.Reply(context, 400, "{\"error\":{\"message\":\"" + TemperatureRejected + "\"}}");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), context.RequestAborted).ContinueWith(_ => { });
        });
        var client = ClientFor(server, Options(server, temperature: 0.4), TimeSpan.FromMilliseconds(400));

        var result = await client.GenerateAsync(Prompt(), CancellationToken.None);

        Assert.Equal(AiFailureKind.Timeout, result.Failure);
        Assert.Equal(0.4, client.CurrentShape.Temperature); // the retry never completed, so nothing is remembered
    }

    [Fact]
    public async Task A_slow_first_answer_is_a_timeout_without_a_retry()
    {
        await using var server = await LocalAiServer.StartAsync(async (context, _) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), context.RequestAborted).ContinueWith(_ => { });
        });

        var result = await ClientFor(server, timeout: TimeSpan.FromMilliseconds(300)).GenerateAsync(Prompt(), CancellationToken.None);

        Assert.Equal(AiFailureKind.Timeout, result.Failure);
        Assert.Single(server.Seen);
    }

    private static Presenta.Domain.SessionSnapshot Alerting() =>
        new(
            Presenta.Domain.SessionState.Running, "Some deck", 11, 20, 900_000, 600_000, 300_000, true, DateTimeOffset.UnixEpoch, 0.55, Presenta.Domain.PaceStatus.Behind,
            9, 1.1, null,
            new Presenta.Domain.Alert(Presenta.Domain.AlertKind.TimeRunningOut, Presenta.Domain.AlertSeverity.Warning, 600_000, 300_000, 9, -2, 1),
            new Presenta.Domain.Recommendation(Presenta.Domain.RecommendationCode.SpeedUpSlightly, 9, 300_000));
}
