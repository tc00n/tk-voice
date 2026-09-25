using TKVoice.Core.Abstractions;
using TKVoice.Core.Settings;
using TKVoice.OpenAI.Realtime;

namespace TKVoice.OpenAI;

/// <summary>Streaming transcription via the OpenAI Realtime API (transcription sessions).</summary>
public sealed class RealtimeTranscriptionService : ITranscriptionService
{
    private readonly OpenAISettings _settings;
    private readonly ICredentialService _credentials;
    private readonly Func<IReadOnlyList<string>> _keywords;
    private readonly ILog _log;
    private readonly Func<string, IRealtimeTransport> _transportFactory;

    /// <param name="keywords">Personal vocabulary sent as recognition hints (FR-019).</param>
    public RealtimeTranscriptionService(OpenAISettings settings, ICredentialService credentials, Func<IReadOnlyList<string>> keywords, ILog log)
        : this(settings, credentials, keywords, log, apiKey => new WebSocketRealtimeTransport(new Uri(settings.RealtimeUrl), apiKey))
    {
    }

    internal RealtimeTranscriptionService(
        OpenAISettings settings,
        ICredentialService credentials,
        Func<IReadOnlyList<string>> keywords,
        ILog log,
        Func<string, IRealtimeTransport> transportFactory)
    {
        _settings = settings;
        _credentials = credentials;
        _keywords = keywords;
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
            Keywords: _keywords(),
            Prompt: null);

        return new RotatingTranscriptionSession(
            () =>
            {
                var session = new RealtimeTranscriptionSession(
                    _transportFactory(apiKey),
                    options,
                    TimeSpan.FromSeconds(_settings.ConnectTimeoutSeconds),
                    TimeSpan.FromMilliseconds(_settings.TrailingSilenceMilliseconds),
                    _log);
                session.Start();
                _log.Debug($"Transcription session started with model {_settings.TranscriptionModel}.");
                return session;
            },
            TimeSpan.FromMinutes(_settings.SessionRotationMinutes),
            TimeSpan.FromMinutes(_settings.SessionRotationMinutes + 5),
            _log);
    }
}
