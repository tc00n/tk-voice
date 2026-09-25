using TKVoice.Core.Abstractions;
using TKVoice.Core.Settings;
using TKVoice.OpenAI.Realtime;

namespace TKVoice.OpenAI;

/// <summary>Streaming transcription via the OpenAI Realtime API (transcription sessions).</summary>
public sealed class RealtimeTranscriptionService : ITranscriptionService
{
    private readonly OpenAISettings _settings;
    private readonly ICredentialService _credentials;
    private readonly ILog _log;
    private readonly Func<string, IRealtimeTransport> _transportFactory;

    public RealtimeTranscriptionService(OpenAISettings settings, ICredentialService credentials, ILog log)
        : this(settings, credentials, log, apiKey => new WebSocketRealtimeTransport(new Uri(settings.RealtimeUrl), apiKey))
    {
    }

    internal RealtimeTranscriptionService(
        OpenAISettings settings,
        ICredentialService credentials,
        ILog log,
        Func<string, IRealtimeTransport> transportFactory)
    {
        _settings = settings;
        _credentials = credentials;
        _log = log;
        _transportFactory = transportFactory;
    }

    public ITranscriptionSession StartSession()
    {
        var apiKey = _credentials.GetOpenAIApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new TranscriptionException("Kein OpenAI API Key hinterlegt.");
        }

        var options = new TranscriptionSessionOptions(
            _settings.TranscriptionModel,
            _settings.TranscriptionDelay,
            _settings.Languages,
            Keywords: [],
            Prompt: null);

        var session = new RealtimeTranscriptionSession(
            _transportFactory(apiKey),
            options,
            TimeSpan.FromSeconds(_settings.ConnectTimeoutSeconds),
            _log);
        session.Start();
        _log.Debug($"Transcription session started with model {_settings.TranscriptionModel}.");
        return session;
    }
}
