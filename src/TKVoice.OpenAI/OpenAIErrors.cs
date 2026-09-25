using System.Net;

namespace TKVoice.OpenAI;

/// <summary>Classifies OpenAI errors and turns them into messages the user can act on.</summary>
internal static class OpenAIErrors
{
    private static readonly HashSet<string> TransientCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "server_error", "rate_limit_exceeded", "timeout", "service_unavailable", "overloaded", "internal_error",
    };

    public static bool IsTransient(string? code) => code is not null && TransientCodes.Contains(code);

    public static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests
        || (int)status >= 500;

    public static string UserMessage(string? code, string? apiMessage, HttpStatusCode? status = null) =>
        (code, status) switch
        {
            ("invalid_api_key", _) or (_, HttpStatusCode.Unauthorized) =>
                "OpenAI API Key ungültig. Bitte im Tray-Menü neu hinterlegen.",
            ("insufficient_quota", _) => "OpenAI-Kontingent erschöpft. Bitte Abrechnung im OpenAI-Konto prüfen.",
            ("model_not_found", _) or (_, HttpStatusCode.NotFound) =>
                "Das konfigurierte OpenAI-Modell ist nicht verfügbar. Bitte Modell in settings.json prüfen.",
            (_, HttpStatusCode.Forbidden) => "Kein Zugriff auf das OpenAI-Modell mit diesem API Key.",
            ("rate_limit_exceeded", _) or (_, HttpStatusCode.TooManyRequests) => "OpenAI ist gerade überlastet (Rate Limit).",
            _ => $"OpenAI-Fehler: {apiMessage ?? code ?? status?.ToString() ?? "unbekannt"}",
        };
}
