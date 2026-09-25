using TKVoice.Core.Abstractions;
using TKVoice.Core.Settings;
using TKVoice.OpenAI.Realtime;

namespace TKVoice.OpenAI;

/// <summary>Streaming transcription via the OpenAI Realtime API (transcription sessions).</summary>
public sealed class RealtimeTranscriptionService : ITranscriptionService
{
    private readonly OpenAISettings _settings;
    private readonly ProcessingSettings _processing;
    private readonly ICredentialService _credentials;
    private readonly Func<IReadOnlyList<string>> _keywords;
    private readonly ILog _log;
    private readonly Func<string, IRealtimeTransport> _transportFactory;

    /// <param name="keywords">Personal vocabulary sent as recognition hints (FR-019).</param>
    public RealtimeTranscriptionService(
        OpenAISettings settings,
        ProcessingSettings processing,
        ICredentialService credentials,
        Func<IReadOnlyList<string>> keywords,
        ILog log)
        : this(settings, processing, credentials, keywords, log, apiKey => new WebSocketRealtimeTransport(new Uri(settings.RealtimeUrl), apiKey))
    {
    }

    internal RealtimeTranscriptionService(
        OpenAISettings settings,
        ProcessingSettings processing,
        ICredentialService credentials,
        Func<IReadOnlyList<string>> keywords,
        ILog log,
        Func<string, IRealtimeTransport> transportFactory)
    {
        _settings = settings;
        _processing = processing;
        _credentials = credentials;
        _keywords = keywords;
        _log = log;
        _transportFactory = transportFactory;
    }

    /// <summary>Realtime sessions end after 60 minutes; a segment without any pause is split before that.</summary>
    private static readonly TimeSpan MaxSegmentDuration = TimeSpan.FromMinutes(55);

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

        return new SegmentedTranscriptionSession(
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
            _processing.MaxRetries,
            TimeSpan.FromSeconds(_processing.TimeoutSeconds),
            MaxSegmentDuration,
            _log);
    }
}
