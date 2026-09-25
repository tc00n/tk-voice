namespace TKVoice.Core.Processing;

/// <summary>
/// Last line of defense for the output contract (§43, FR-009): the model must return only the
/// cleaned dictation. Strips wrapping code fences and rejects output that is far longer than the
/// transcript, which indicates the model answered or expanded instead of cleaning up.
/// </summary>
public static class SmartOutputGuard
{
    public static bool TryAccept(string transcript, string output, out string accepted)
    {
        accepted = StripWrappingFence(output.Trim());
        if (accepted.Length == 0)
        {
            return false;
        }

        // Formatting (bullets, line breaks, "25 %") adds a little; answering or expanding adds a lot.
        // Short answers can still slip through; the prompt is the primary safeguard.
        var maxLength = (int)(transcript.Length * 1.3) + 40;
        return accepted.Length <= maxLength;
    }

    private static string StripWrappingFence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal) || !text.EndsWith("```", StringComparison.Ordinal) || text.Length < 6)
        {
            return text;
        }

        var firstNewline = text.IndexOf('\n');
        if (firstNewline < 0)
        {
            return text;
        }

        return text[(firstNewline + 1)..^3].Trim();
    }
}
