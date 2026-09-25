using TKVoice.Core.Dictation;
using TKVoice.Core.Settings;

namespace TKVoice.Tests.Dictation;

public class InsertionMethodSelectorTests
{
    [Fact]
    public void Paste_is_the_default()
    {
        Assert.Equal(InsertionMethod.Paste, InsertionMethodSelector.Select(new InsertionSettings(), "notepad"));
    }

    [Fact]
    public void Per_process_override_types_instead()
    {
        var settings = new InsertionSettings { TypeInsteadOfPasteProcesses = ["mstsc"] };

        Assert.Equal(InsertionMethod.Type, InsertionMethodSelector.Select(settings, "MSTSC"));
        Assert.Equal(InsertionMethod.Paste, InsertionMethodSelector.Select(settings, "notepad"));
    }

    [Fact]
    public void Global_type_method_is_respected_and_unknown_values_fall_back_to_paste()
    {
        Assert.Equal(InsertionMethod.Type, InsertionMethodSelector.Select(new InsertionSettings { Method = "type" }, "notepad"));
        Assert.Equal(InsertionMethod.Paste, InsertionMethodSelector.Select(new InsertionSettings { Method = "bogus" }, "notepad"));
    }
}
