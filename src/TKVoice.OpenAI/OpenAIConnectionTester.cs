using System.Net;
using System.Net.Http.Headers;
using TKVoice.Core.Settings;
using TKVoice.OpenAI.Smart;

namespace TKVoice.OpenAI;

/// <summary>"Verbindung testen": checks the API key and that both configured models are available.</summary>
public static class OpenAIConnectionTester
{
    public static async Task<IReadOnlyList<string>> TestAsync(
        string apiKey,
        OpenAISettings settings,
        HttpMessageHandler? handler = null,
        CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: handler is null) { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var baseUrl = new Uri(settings.ResponsesUrl).GetLeftPart(UriPartial.Authority) + "/v1/models/";

        var results = new List<string>();
        foreach (var (role, model) in new[] { ("Transkription", settings.TranscriptionModel), ("Smart Processing", settings.SmartProcessingModel) })
        {
            try
            {
                using var response = await http.GetAsync(baseUrl + Uri.EscapeDataString(model), cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    results.Add($"✓ {role}: {model} verfügbar");
                }
                else
                {
                    var (code, message) = ResponsesProtocol.ParseError(await response.Content.ReadAsStringAsync(cancellationToken));
                    results.Add($"✗ {role}: {model} – {OpenAIErrors.UserMessage(code, message, response.StatusCode)}");
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        break; // the second check would say the same
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                results.Add($"✗ Keine Verbindung zu OpenAI: {ex.Message}");
                break;
            }
        }

        return results;
    }
}
