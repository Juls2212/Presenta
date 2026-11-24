using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Presenta.Ai;

/// <summary>The request fields that depend on the provider and model (ADR-80). It holds no secrets.</summary>
public sealed record AiRequestShape(AiTokenLimitParameter TokenParameter, int MaxTokens, double? Temperature, string? ReasoningEffort);

/// <summary>
/// Chat-completions client for any provider that speaks the common <c>POST {baseUrl}/chat/completions</c> shape with
/// Bearer authentication (ADR-72). Only <see cref="HttpClient"/> and <see cref="System.Text.Json"/> are used. The key goes
/// into the Authorization header of each request and nowhere else; failures are reported as <see cref="AiFailureKind"/>
/// only, so nothing the provider or the network says can leak into a log. Redirects must be turned off in the handler
/// (the app does), so the key is never forwarded to another address.
/// <para>
/// The request carries <c>max_completion_tokens</c> and no temperature by default. When the provider answers 400 naming an
/// unsupported or unrecognized parameter that was sent, the request is repeated once without that parameter (or with the
/// other token-limit name) and the shape that worked is kept for the rest of the process (ADR-80).
/// </para>
/// </summary>
public sealed class OpenAiCompatibleClient : IAiTextClient
{
    private const int MaxErrorBodyBytes = 8 * 1024;

    private readonly HttpClient _http;
    private readonly AiEndpointOptions _options;
    private readonly Uri _endpoint;
    private readonly TimeProvider _time;

    // The shape that works with this provider. It starts from the options and changes only when the provider names an
    // unsupported parameter. A reference swap, so a concurrent read never sees a half-built shape.
    private volatile AiRequestShape _shape;

    public OpenAiCompatibleClient(HttpClient http, AiEndpointOptions options, TimeProvider? time = null)
    {
        _http = http;
        _options = options;
        _time = time ?? TimeProvider.System;
        _endpoint = new Uri(options.BaseUrl.AbsoluteUri.TrimEnd('/') + "/chat/completions");
        _shape = new AiRequestShape(options.TokenLimitParameter, options.MaxTokens, options.Temperature, options.ReasoningEffort);
    }

    /// <summary>The shape in use now, after any adaptation. For tests and diagnostics.</summary>
    public AiRequestShape CurrentShape => _shape;

    /// <summary>The exact JSON body of a request. Public so a test can pin its shape.</summary>
    public static string BuildRequestJson(string model, AiPrompt prompt, AiRequestShape shape)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);
            writer.WriteStartArray("messages");
            WriteMessage(writer, "system", prompt.System);
            WriteMessage(writer, "user", prompt.User);
            writer.WriteEndArray();
            writer.WriteNumber(TokenName(shape.TokenParameter), shape.MaxTokens);
            if (shape.Temperature is { } temperature)
            {
                writer.WriteNumber("temperature", temperature);
            }

            if (!string.IsNullOrEmpty(shape.ReasoningEffort))
            {
                writer.WriteString("reasoning_effort", shape.ReasoningEffort);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public async Task<AiTextResult> GenerateAsync(AiPrompt prompt, CancellationToken cancellationToken)
    {
        // One time budget for the whole answer, the adaptive retry included.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        var attempts = 1;
        try
        {
            var shape = _shape;
            var first = await SendAsync(prompt, shape, timeout.Token).ConfigureAwait(false);
            if (first.BadRequestBody is null)
            {
                return first.Result;
            }

            var adjusted = Adjust(shape, first.BadRequestBody);
            if (adjusted is null)
            {
                return first.Result; // a 400 that does not name a parameter we sent: not retried
            }

            attempts = 2;
            var second = await SendAsync(prompt, adjusted, timeout.Token).ConfigureAwait(false);
            if (second.Accepted)
            {
                _shape = adjusted; // remembered for the rest of the process
            }

            return second.Result with { Attempts = 2 };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AiTextResult.Fail(AiFailureKind.Timeout) with { Attempts = attempts };
        }
        catch (HttpRequestException)
        {
            return AiTextResult.Fail(AiFailureKind.Network) with { Attempts = attempts };
        }
    }

    /// <summary>
    /// The shape without the parameter a 400 body names as unsupported or unrecognized, or null when the body names none of
    /// the parameters that were sent. Only the body text is inspected; it is never kept or logged.
    /// </summary>
    internal static AiRequestShape? Adjust(AiRequestShape shape, string errorBody)
    {
        var text = errorBody.ToLowerInvariant();
        if (!(text.Contains("unsupported", StringComparison.Ordinal)
              || text.Contains("unrecognized", StringComparison.Ordinal)
              || text.Contains("unknown", StringComparison.Ordinal)
              || text.Contains("not support", StringComparison.Ordinal)))
        {
            return null;
        }

        var adjusted = shape;
        if (text.Contains(TokenName(shape.TokenParameter), StringComparison.Ordinal))
        {
            adjusted = adjusted with
            {
                TokenParameter = shape.TokenParameter == AiTokenLimitParameter.MaxTokens
                    ? AiTokenLimitParameter.MaxCompletionTokens
                    : AiTokenLimitParameter.MaxTokens,
            };
        }

        if (shape.Temperature is not null && text.Contains("temperature", StringComparison.Ordinal))
        {
            adjusted = adjusted with { Temperature = null };
        }

        if (shape.ReasoningEffort is not null && text.Contains("reasoning_effort", StringComparison.Ordinal))
        {
            adjusted = adjusted with { ReasoningEffort = null };
        }

        return adjusted == shape ? null : adjusted;
    }

    private static string TokenName(AiTokenLimitParameter parameter) =>
        parameter == AiTokenLimitParameter.MaxTokens ? "max_tokens" : "max_completion_tokens";

    private async Task<SendOutcome> SendAsync(AiPrompt prompt, AiRequestShape shape, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(BuildRequestJson(_options.Model, prompt, shape), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        if (status is 401 or 403)
        {
            return new SendOutcome(AiTextResult.Fail(AiFailureKind.Unauthorized), false, null);
        }

        if (status == 429)
        {
            return new SendOutcome(AiTextResult.Fail(AiFailureKind.RateLimited, ReadRetryAfter(response)), false, null);
        }

        if (status >= 500)
        {
            return new SendOutcome(AiTextResult.Fail(AiFailureKind.ServerError), false, null);
        }

        if (status == 400)
        {
            var errorBody = await ReadBoundedAsync(response, MaxErrorBodyBytes, token).ConfigureAwait(false);
            return new SendOutcome(AiTextResult.Fail(AiFailureKind.Rejected), false, errorBody ?? string.Empty);
        }

        if (status is < 200 or >= 300)
        {
            return new SendOutcome(AiTextResult.Fail(AiFailureKind.Rejected), false, null);
        }

        var body = await ReadBoundedAsync(response, AiLimits.MaxResponseBytes, token).ConfigureAwait(false);
        return new SendOutcome(body is null ? AiTextResult.Fail(AiFailureKind.Malformed) : ParseAnswer(body), true, null);
    }

    private sealed record SendOutcome(AiTextResult Result, bool Accepted, string? BadRequestBody);

    private static void WriteMessage(Utf8JsonWriter writer, string role, string content)
    {
        writer.WriteStartObject();
        writer.WriteString("role", role);
        writer.WriteString("content", content);
        writer.WriteEndObject();
    }

    private TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null)
        {
            return null;
        }

        if (header.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (header.Date is { } date)
        {
            var wait = date - _time.GetUtcNow();
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        return null;
    }

    private static async Task<string?> ReadBoundedAsync(HttpResponseMessage response, int limit, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is { } declared && declared > limit)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static AiTextResult ParseAnswer(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0
                && choices[0].ValueKind == JsonValueKind.Object
                && choices[0].TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.Object)
            {
                // A well formed answer with no visible text, typically a reasoning model that used the whole token limit.
                if (!message.TryGetProperty("content", out var content) || content.ValueKind == JsonValueKind.Null)
                {
                    return AiTextResult.Fail(AiFailureKind.NoText);
                }

                if (content.ValueKind == JsonValueKind.String)
                {
                    var text = content.GetString();
                    return string.IsNullOrWhiteSpace(text) ? AiTextResult.Fail(AiFailureKind.NoText) : AiTextResult.Success(text);
                }
            }
        }
        catch (JsonException)
        {
            // Falls through to Malformed.
        }

        return AiTextResult.Fail(AiFailureKind.Malformed);
    }
}
