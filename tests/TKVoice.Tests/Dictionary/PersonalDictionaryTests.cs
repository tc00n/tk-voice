using TKVoice.Core.Dictionary;

namespace TKVoice.Tests.Dictionary;

public class PersonalDictionaryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tkvoice-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "dictionary.json");

    [Fact]
    public void Add_update_remove_persist_across_instances()
    {
        var dictionary = new PersonalDictionary(FilePath);
        Assert.True(dictionary.Add("NEONEX"));
        Assert.True(dictionary.Add(" Shopfloor Management "));
        Assert.True(dictionary.Add("FPY"));
        Assert.True(dictionary.Update("FPY", "First Pass Yield"));
        Assert.True(dictionary.Remove("NEONEX"));

        var reloaded = new PersonalDictionary(FilePath);
        Assert.Equal(["Shopfloor Management", "First Pass Yield"], reloaded.Terms);
    }

    [Fact]
    public void Duplicates_are_rejected_case_insensitively()
    {
        var dictionary = new PersonalDictionary(FilePath);
        Assert.True(dictionary.Add("NEONEX"));
        Assert.False(dictionary.Add("neonex"));
        Assert.Single(dictionary.Terms);
    }

    [Fact]
    public void Update_to_existing_term_is_rejected_but_case_fix_is_allowed()
    {
        var dictionary = new PersonalDictionary(FilePath);
        dictionary.Add("Neonex");
        dictionary.Add("FPY");

        Assert.False(dictionary.Update("FPY", "neonex"));
        Assert.True(dictionary.Update("Neonex", "NEONEX"));
        Assert.Equal(["NEONEX", "FPY"], dictionary.Terms);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    [InlineData("zwei\nZeilen")]
    [InlineData("Ein ganzer Satz, der viel zu lang für einen einzelnen Wörterbuchbegriff ist und so weiter.")]
    public void Invalid_terms_are_rejected(string term)
    {
        Assert.False(PersonalDictionary.TryNormalize(term, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Surrounding_quotes_are_stripped()
    {
        Assert.True(PersonalDictionary.TryNormalize("„NEONEX“", out var term, out _));
        Assert.Equal("NEONEX", term);
    }

    [Fact]
    public void Changed_is_raised_on_modification()
    {
        var dictionary = new PersonalDictionary(FilePath);
        var raised = 0;
        dictionary.Changed += (_, _) => raised++;

        dictionary.Add("A1");
        dictionary.Add("a1");
        dictionary.Remove("A1");

        Assert.Equal(2, raised);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
