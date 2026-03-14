using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NotepadSharp.App.Services;

public sealed record OpenAiResponseText(string OutputText, string Model, string? ResponseId);

public sealed class OpenAiResponsesClient
{
    private static readonly HttpClient SharedHttpClient = new();
    private readonly HttpClient _httpClient;

    public OpenAiResponsesClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public async Task<OpenAiResponseText> CreateTextResponseAsync(
        AiProviderSettings settings,
        string developerPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var normalized = AiProviderConfigLogic.Normalize(settings);
        var apiKey = Environment.GetEnvironmentVariable(normalized.ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"Env var '{normalized.ApiKeyEnvironmentVariable}' is not set.");
        }

        using var request = BuildRequest(normalized, developerPrompt, userPrompt, apiKey);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var message = TryExtractError(payload);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
                ? $"OpenAI request failed with status {(int)response.StatusCode}."
                : $"OpenAI request failed: {message}");
        }

        var result = ParseResponse(payload);
        if (string.IsNullOrWhiteSpace(result.OutputText))
        {
            throw new InvalidOperationException("OpenAI response did not include text output.");
        }

        return result;
    }

    public static HttpRequestMessage BuildRequest(
        AiProviderSettings settings,
        string developerPrompt,
        string userPrompt,
        string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(BuildRequestJson(settings, developerPrompt, userPrompt), Encoding.UTF8, "application/json");
        return request;
    }

    public static string BuildRequestJson(
        AiProviderSettings settings,
        string developerPrompt,
        string userPrompt)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = settings.Model,
            ["input"] = new object[]
            {
                BuildMessage("developer", developerPrompt),
                BuildMessage("user", userPrompt),
            },
            ["max_output_tokens"] = 1800,
        };

        return JsonSerializer.Serialize(payload);
    }

    public static OpenAiResponseText ParseResponse(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var outputBuilder = new StringBuilder();
        if (root.TryGetProperty("output", out var outputArray) && outputArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var outputItem in outputArray.EnumerateArray())
            {
                if (!outputItem.TryGetProperty("content", out var contentArray) || contentArray.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var content in contentArray.EnumerateArray())
                {
                    if (content.TryGetProperty("type", out var type)
                        && string.Equals(type.GetString(), "output_text", StringComparison.Ordinal)
                        && content.TryGetProperty("text", out var text))
                    {
                        outputBuilder.AppendLine(text.GetString());
                    }
                }
            }
        }

        var model = root.TryGetProperty("model", out var modelElement)
            ? modelElement.GetString() ?? string.Empty
            : string.Empty;
        var responseId = root.TryGetProperty("id", out var idElement)
            ? idElement.GetString()
            : null;

        return new OpenAiResponseText(outputBuilder.ToString().Trim(), model, responseId);
    }

    public static string? TryExtractError(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("error", out var error))
            {
                return null;
            }

            if (error.TryGetProperty("message", out var messageElement))
            {
                return messageElement.GetString();
            }

            return error.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static object BuildMessage(string role, string text)
        => new Dictionary<string, object?>
        {
            ["role"] = role,
            ["content"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "input_text",
                    ["text"] = text,
                },
            },
        };
}
