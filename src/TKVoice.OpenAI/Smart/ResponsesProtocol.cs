using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TKVoice.OpenAI.Smart;

/// <summary>Request building and response parsing for the OpenAI Responses API.</summary>
internal static class ResponsesProtocol
{
    public static string BuildRequest(string model, string? reasoningEffort, string? serviceTier, string instructions, string input, int maxOutputTokens)
    {
        var request = new JsonObject
        {
            ["model"] = model,
            ["instructions"] = instructions,
            ["input"] = input,
            ["max_output_tokens"] = maxOutputTokens,

            // Dictations must not be retained by the provider beyond processing (NFR-003).
            ["store"] = false,
        };

        if (!string.IsNullOrWhiteSpace(serviceTier))
        {
            request["service_tier"] = serviceTier;
        }

        if (!string.IsNullOrWhiteSpace(reasoningEffort))
        {
            request["reasoning"] = new JsonObject { ["effort"] = reasoningEffort };
        }

        return request.ToJsonString();
    }

    public static ResponsesResult ParseResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (status is not null && status != "completed")
        {
            var reason = root.TryGetProperty("incomplete_details", out var details) && details.ValueKind == JsonValueKind.Object
                && details.TryGetProperty("reason", out var r) ? r.GetString() : null;
            throw new InvalidOperationException($"Response status '{status}'{(reason is null ? string.Empty : $" ({reason})")}.");
        }

        var text = new StringBuilder();
        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (GetString(item, "type") != "message" || !item.TryGetProperty("content", out var content))
                {
                    continue;
                }

                foreach (var part in content.EnumerateArray())
                {
                    if (GetString(part, "type") == "output_text")
                    {
                        text.Append(GetString(part, "text"));
                    }
                }
            }
        }

        int? inputTokens = null, outputTokens = null;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            inputTokens = usage.TryGetProperty("input_tokens", out var i) ? i.GetInt32() : null;
            outputTokens = usage.TryGetProperty("output_tokens", out var o) ? o.GetInt32() : null;
        }

        return new ResponsesResult(text.ToString(), GetString(root, "id"), inputTokens, outputTokens);
    }

    public static string? ParseErrorMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                ? GetString(error, "message")
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

internal sealed record ResponsesResult(string Text, string? ResponseId, int? InputTokens, int? OutputTokens);
