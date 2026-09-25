using TKVoice.Core.Dictionary;

namespace TKVoice.Tests.Dictionary;

public class SpelledTermDetectorTests
{
    [Fact]
    public void Detects_hyphen_spelled_term()
    {
        Assert.Equal(["NEONEX"], SpelledTermDetector.Detect("NEONEX, geschrieben N-E-O-N-E-X.", "NEONEX"));
    }

    [Fact]
    public void Uses_capitalization_from_inserted_text()
    {
        Assert.Equal(["Kuhn"], SpelledTermDetector.Detect("Mein Name ist Kuhn, buchstabiert K U H N.", "Mein Name ist Kuhn."));
    }

    [Fact]
    public void Falls_back_to_spelled_letters_when_not_in_final_text()
    {
        Assert.Equal(["FPY"], SpelledTermDetector.Detect("Das heißt FPY, geschrieben F-P-Y.", "Das heißt First Pass Yield."));
    }

    [Fact]
    public void Stops_at_the_first_real_word_after_the_letters()
    {
        Assert.Equal(["ABC"], SpelledTermDetector.Detect("Firma ABC, geschrieben A B C und dann weiter.", "Firma ABC und dann weiter."));
    }

    [Theory]
    [InlineData("Das habe ich so geschrieben.")]
    [InlineData("Wie wird das geschrieben, mit Doppel-S?")]
    [InlineData("Ich habe ihm geschrieben A.")]
    [InlineData("Keine Buchstaben hier.")]
    public void Ignores_text_without_explicit_spelling(string transcript)
    {
        Assert.Empty(SpelledTermDetector.Detect(transcript, transcript));
    }
}
