using TKVoice.Core.Processing;

namespace TKVoice.Tests.Processing;

public class SmartSkipHeuristicTests
{
    [Theory]
    [InlineData("Ich bin gleich da.")]
    [InlineData("Danke, das passt so.")]
    [InlineData("Kannst du mir den Bericht schicken?")]
    [InlineData("Sounds good, see you tomorrow.")]
    [InlineData("Das Fenster ist offen.")]
    public void Clean_short_text_can_skip(string transcript)
    {
        Assert.True(SmartSkipHeuristic.CanSkip(transcript));
    }

    [Theory]
    [InlineData("Ähm also wir müssen quasi morgen äh mit Peter sprechen.")] // fillers (AC-11)
    [InlineData("Wir treffen uns Dienstag, nein Mittwoch um vierzehn Uhr.")] // correction, numbers (AC-03)
    [InlineData("Um 13 Uhr, ich meine 14 Uhr.")] // correction phrase
    [InlineData("Wir haben fünfundzwanzig Prozent Ausschuss.")] // numbers (AC-04)
    [InlineData("NEONEX, geschrieben N-E-O-N-E-X.")] // spelling (AC-05)
    [InlineData("Das ist N E O N E X.")] // spelling without trigger
    [InlineData("Hallo Peter neuer Absatz wie geht es dir")] // command
    [InlineData("Mach daraus drei Bulletpoints.")] // structure command + number
    [InlineData("Das das ist gut.")] // repetition
    [InlineData("Wir haben dreitausendzweihundert Euro ausgegeben.")] // compound number
    [InlineData("")]
    public void Text_that_needs_cleanup_is_not_skipped(string transcript)
    {
        Assert.False(SmartSkipHeuristic.CanSkip(transcript));
    }

    [Fact]
    public void Long_text_is_not_skipped()
    {
        var longText = string.Join(" ", Enumerable.Repeat("Wort Satz", 15));
        Assert.False(SmartSkipHeuristic.CanSkip(longText));
    }
}
