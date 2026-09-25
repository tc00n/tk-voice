using System.Text.Json;
using System.Text.Json.Nodes;

namespace TKVoice.OpenAI.Realtime;

/// <summary>Builds client events and parses server events of the Realtime transcription API.</summary>
internal static class RealtimeProtocol
{
    public const string CommitEmptyErrorCode = "input_audio_buffer_commit_empty";

    public static string SessionUpdate(TranscriptionSessionOptions options)
    {
        var transcription = new JsonObject { ["model"] = options.Model };
        if (!string.IsNullOrWhiteSpace(options.Delay))
        {
            transcription["delay"] = options.Delay;
        }

        if (options.Languages.Count > 0)
        {
            transcription["languages"] = ToArray(options.Languages);
        }

        if (options.Keywords.Count > 0)
        {
            transcription["keywords"] = ToArray(options.Keywords);
        }

        if (!string.IsNullOrWhiteSpace(options.Prompt))
        {
            transcription["prompt"] = options.Prompt;
        }

        var message = new JsonObject
        {
            ["type"] = "session.update",
            ["session"] = new JsonObject
            {
                ["type"] = "transcription",
                ["audio"] = new JsonObject
                {
                    ["input"] = new JsonObject
                    {
                        ["format"] = new JsonObject { ["type"] = "audio/pcm", ["rate"] = 24_000 },
                        ["transcription"] = transcription,
                        ["turn_detection"] = null,
                    },
                },
            },
        };

        return message.ToJsonString();
    }

    public static string AppendAudio(ReadOnlySpan<byte> pcm) =>
        $$"""{"type":"input_audio_buffer.append","audio":"{{Convert.ToBase64String(pcm)}}"}""";

    public static string Commit() => """{"type":"input_audio_buffer.commit"}""";

    public static ServerEvent Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var type = GetString(root, "type") ?? string.Empty;

        return type switch
        {
            "input_audio_buffer.committed" => new ServerEvent.Committed(GetString(root, "item_id") ?? string.Empty),
            "conversation.item.input_audio_transcription.delta" => new ServerEvent.TranscriptDelta(
                GetString(root, "item_id") ?? string.Empty,
                GetString(root, "delta") ?? string.Empty),
            "conversation.item.input_audio_transcription.completed" => new ServerEvent.TranscriptCompleted(
                GetString(root, "item_id") ?? string.Empty,
                GetString(root, "transcript") ?? string.Empty),
            "conversation.item.input_audio_transcription.failed" => new ServerEvent.Error(
                ErrorField(root, "code") ?? "transcription_failed",
                ErrorField(root, "message") ?? "Transcription failed."),
            "error" => new ServerEvent.Error(
                ErrorField(root, "code") ?? ErrorField(root, "type") ?? "unknown",
                ErrorField(root, "message") ?? "Unknown error."),
            _ => new ServerEvent.Other(type),
        };
    }

    private static JsonArray ToArray(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? ErrorField(JsonElement root, string property) =>
        root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object ? GetString(error, property) : null;
}

internal abstract record ServerEvent
{
    public sealed record Committed(string ItemId) : ServerEvent;

    public sealed record TranscriptDelta(string ItemId, string Delta) : ServerEvent;

    public sealed record TranscriptCompleted(string ItemId, string Transcript) : ServerEvent;

    public sealed record Error(string Code, string Message) : ServerEvent;

    public sealed record Other(string Type) : ServerEvent;
}

internal sealed record TranscriptionSessionOptions(
    string Model,
    string? Delay,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Keywords,
    string? Prompt);
