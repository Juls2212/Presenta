using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Presenta.Ai;

/// <summary>
/// Chat-completions client for any provider that speaks the common <c>POST {baseUrl}/chat/completions</c> shape with
/// Bearer authentication (ADR-72). Only <see cref="HttpClient"/> and <see cref="System.Text.Json"/> are used. The key goes
/// into the Authorization header of each request and nowhere else; failures are reported as <see cref="AiFailureKind"/>
/// only, so nothing the provider or the network says can leak into a log. Redirects must be turned off in the handler
/// (the app does), so the key is never forwarded to another address.
/// </summary>
public sealed class OpenAiCompatibleClient : IAiTextClient
{
    private readonly HttpClient _http;
    private readonly AiEndpointOptions _options;
    private readonly Uri _endpoint;

    private readonly TimeProvider _time;

    public OpenAiCompatibleClient(HttpClient http, AiEndpointOptions options, TimeProvider? time = null)
    {
        _http = http;
        _options = options;
        _time = time ?? TimeProvider.System;
        _endpoint = new Uri(options.BaseUrl.AbsoluteUri.TrimEnd('/') + "/chat/completions");
    }

    /// <summary>The exact JSON body of a request. Public so a test can pin its shape.</summary>
    public static string BuildRequestJson(string model, AiPrompt prompt, int maxTokens, double temperature)
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
            writer.WriteNumber("max_tokens", maxTokens);
            writer.WriteNumber("temperature", temperature);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public async Task<AiTextResult> GenerateAsync(AiPrompt prompt, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(
                    BuildRequestJson(_options.Model, prompt, _options.MaxTokens, _options.Temperature),
                    Encoding.UTF8,
                    "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status is 401 or 403)
            {
                return AiTextResult.Fail(AiFailureKind.Unauthorized);
            }

            if (status == 429)
            {
                return AiTextResult.Fail(AiFailureKind.RateLimited, ReadRetryAfter(response));
            }

            if (status >= 500)
            {
                return AiTextResult.Fail(AiFailureKind.ServerError);
            }

            if (status is < 200 or >= 300)
            {
                return AiTextResult.Fail(AiFailureKind.Rejected);
            }

            var body = await ReadBoundedAsync(response, timeout.Token).ConfigureAwait(false);
            return body is null ? AiTextResult.Fail(AiFailureKind.Malformed) : ParseAnswer(body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AiTextResult.Fail(AiFailureKind.Timeout);
        }
        catch (HttpRequestException)
        {
            return AiTextResult.Fail(AiFailureKind.Network);
        }
    }

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

    private static async Task<string?> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > AiLimits.MaxResponseBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > AiLimits.MaxResponseBytes)
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
                && message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String
                && content.GetString() is { Length: > 0 } text)
            {
                return AiTextResult.Success(text);
            }
        }
        catch (JsonException)
        {
            // Falls through to Malformed.
        }

        return AiTextResult.Fail(AiFailureKind.Malformed);
    }
}
