using System.Text.Json;
using TKVoice.OpenAI.Realtime;

namespace TKVoice.Tests.OpenAI;

public class RealtimeProtocolTests
{
    [Fact]
    public void Session_update_configures_transcription_session_without_turn_detection()
    {
        var json = RealtimeProtocol.SessionUpdate(new TranscriptionSessionOptions(
            "gpt-live-transcribe", "low", ["de", "en"], ["NEONEX", "FPY"], null));

        using var doc = JsonDocument.Parse(json);
        var session = doc.RootElement.GetProperty("session");
        Assert.Equal("session.update", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("transcription", session.GetProperty("type").GetString());

        var input = session.GetProperty("audio").GetProperty("input");
        Assert.Equal("audio/pcm", input.GetProperty("format").GetProperty("type").GetString());
        Assert.Equal(24_000, input.GetProperty("format").GetProperty("rate").GetInt32());
        Assert.Equal(JsonValueKind.Null, input.GetProperty("turn_detection").ValueKind);

        var transcription = input.GetProperty("transcription");
        Assert.Equal("gpt-live-transcribe", transcription.GetProperty("model").GetString());
        Assert.Equal("low", transcription.GetProperty("delay").GetString());
        Assert.Equal(["de", "en"], transcription.GetProperty("languages").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["NEONEX", "FPY"], transcription.GetProperty("keywords").EnumerateArray().Select(e => e.GetString()));
        Assert.False(transcription.TryGetProperty("prompt", out _));
    }

    [Fact]
    public void Append_audio_encodes_pcm_as_base64()
    {
        using var doc = JsonDocument.Parse(RealtimeProtocol.AppendAudio([1, 2, 3]));
        Assert.Equal("input_audio_buffer.append", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("AQID", doc.RootElement.GetProperty("audio").GetString());
    }

    [Fact]
    public void Parses_completed_transcript()
    {
        var parsed = RealtimeProtocol.Parse(
            """{"type":"conversation.item.input_audio_transcription.completed","item_id":"item_1","content_index":0,"transcript":"Hallo Welt"}""");
        Assert.Equal(new ServerEvent.TranscriptCompleted("item_1", "Hallo Welt"), parsed);
    }

    [Fact]
    public void Parses_error_with_code()
    {
        var parsed = RealtimeProtocol.Parse(
            """{"type":"error","error":{"type":"invalid_request_error","code":"input_audio_buffer_commit_empty","message":"buffer too small"}}""");
        Assert.Equal(new ServerEvent.Error(RealtimeProtocol.CommitEmptyErrorCode, "buffer too small"), parsed);
    }

    [Fact]
    public void Unknown_events_are_tolerated()
    {
        Assert.Equal(new ServerEvent.Other("session.created"), RealtimeProtocol.Parse("""{"type":"session.created","session":{}}"""));
    }
}
