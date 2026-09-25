using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using TKVoice.Core.Abstractions;
using TKVoice.Core.Processing;
using TKVoice.Core.Settings;

namespace TKVoice.OpenAI.Smart;

/// <summary>Smart processing via the OpenAI Responses API. Logs sizes, ids and latency, never text (NFR-006).</summary>
public sealed class OpenAISmartTextProcessor : ISmartTextProcessor, IDisposable
{
    private readonly OpenAISettings _settings;
    private readonly ICredentialService _credentials;
    private readonly Func<IReadOnlyList<string>> _vocabulary;
    private readonly ILog _log;
    private readonly HttpClient _http;

    public OpenAISmartTextProcessor(
        OpenAISettings settings,
        ICredentialService credentials,
        Func<IReadOnlyList<string>> vocabulary,
        ILog log,
        HttpMessageHandler? handler = null)
    {
        _settings = settings;
        _credentials = credentials;
        _vocabulary = vocabulary;
        _log = log;

        // Kept alive across dictations so the TLS connection is reused; timeouts come from the caller.
        _http = new HttpClient(handler ?? new SocketsHttpHandler { PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5) })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public async Task<string> ProcessAsync(TextProcessingRequest request, CancellationToken cancellationToken)
    {
        var apiKey = _credentials.GetOpenAIApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new SmartProcessingException("Kein OpenAI API Key hinterlegt.");
        }

        var body = ResponsesProtocol.BuildRequest(
            _settings.SmartProcessingModel,
            _settings.SmartProcessingReasoningEffort,
            _settings.SmartProcessingServiceTier,
            SmartProcessingPrompt.Instructions,
            SmartProcessingPrompt.BuildInput(request, _vocabulary()),
            MaxOutputTokensFor(request.Transcript));

        using var message = new HttpRequestMessage(HttpMethod.Post, _settings.ResponsesUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var stopwatch = Stopwatch.StartNew();
        using var response = await _http.SendAsync(message, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var requestId = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null;

        if (!response.IsSuccessStatusCode)
        {
            _log.Warn($"Smart processing HTTP {(int)response.StatusCode}, request {requestId}.");
            throw new SmartProcessingException(
                $"OpenAI-Fehler {(int)response.StatusCode}: {ResponsesProtocol.ParseErrorMessage(json) ?? response.ReasonPhrase}");
        }

        var result = ResponsesProtocol.ParseResponse(json);
        _log.Info($"Smart processing model {_settings.SmartProcessingModel}, request {requestId}, " +
                  $"{stopwatch.ElapsedMilliseconds} ms, tokens in {result.InputTokens} / out {result.OutputTokens}.");
        return result.Text;
    }

    /// <summary>
    /// Opens (or keeps alive) the pooled TLS connection with a cheap unauthenticated request, so the
    /// real request after recording does not pay for DNS and TLS handshakes. The response is ignored.
    /// </summary>
    public void Warmup()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var request = new HttpRequestMessage(HttpMethod.Head, _settings.ResponsesUrl);
                using var response = await _http.SendAsync(request, timeout.Token);
            }
            catch (Exception ex)
            {
                _log.Debug($"Smart processing warm-up failed: {ex.GetType().Name}.");
            }
        });
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Generous headroom over the transcript size; formatting never multiplies the text.</summary>
    private static int MaxOutputTokensFor(string transcript) => Math.Clamp(transcript.Length / 2 + 500, 1_000, 32_000);
}
