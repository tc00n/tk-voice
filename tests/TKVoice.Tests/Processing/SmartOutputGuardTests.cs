using TKVoice.Core.Processing;

namespace TKVoice.Tests.Processing;

public class SmartOutputGuardTests
{
    [Fact]
    public void Accepts_cleaned_text()
    {
        Assert.True(SmartOutputGuard.TryAccept("Ähm wir haben fünfundzwanzig Prozent Ausschuss", "Wir haben 25 % Ausschuss.", out var accepted));
        Assert.Equal("Wir haben 25 % Ausschuss.", accepted);
    }

    [Fact]
    public void Accepts_formatting_that_adds_a_little()
    {
        const string transcript = "Einkaufsliste Milch Brot Eier Butter";
        const string output = "Einkaufsliste\n\n- Milch\n- Brot\n- Eier\n- Butter";
        Assert.True(SmartOutputGuard.TryAccept(transcript, output, out _));
    }

    [Fact]
    public void Strips_wrapping_code_fence()
    {
        Assert.True(SmartOutputGuard.TryAccept("hallo welt", "```text\nHallo Welt.\n```", out var accepted));
        Assert.Equal("Hallo Welt.", accepted);
    }

    [Fact]
    public void Rejects_answers_that_are_much_longer_than_the_dictation()
    {
        const string transcript = "Wie spät ist es?";
        const string answer = "Es ist aktuell 14:32 Uhr. Wenn du möchtest, kann ich dir auch die Uhrzeit in anderen Zeitzonen nennen.";
        Assert.False(SmartOutputGuard.TryAccept(transcript, answer, out _));
    }

    [Fact]
    public void Rejects_empty_output()
    {
        Assert.False(SmartOutputGuard.TryAccept("Hallo", "   ", out _));
    }
}
