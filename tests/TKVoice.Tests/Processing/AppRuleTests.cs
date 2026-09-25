using TKVoice.Core.Processing;

namespace TKVoice.Tests.Processing;

public class AppRuleTests
{
    [Theory]
    [InlineData("ms-teams", "Chat")]
    [InlineData("OUTLOOK", "E-Mail")]
    [InlineData("outlook", "E-Mail")]
    [InlineData("WINWORD", "Dokument")]
    [InlineData("POWERPNT", "Präsentation")]
    [InlineData("Code", "Entwicklung")]
    [InlineData("WindowsTerminal", "Entwicklung")]
    [InlineData("claude", "Entwicklung")]
    public void Default_rules_match_known_applications(string process, string expectedRule)
    {
        Assert.Equal(expectedRule, AppRule.Find(AppRule.Defaults(), process)?.Name);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("Notepad")]
    public void Other_applications_use_global_smart_mode(string process)
    {
        Assert.Null(AppRule.Find(AppRule.Defaults(), process));
    }

    [Fact]
    public void Every_default_rule_has_style_and_processes()
    {
        Assert.All(AppRule.Defaults(), rule =>
        {
            Assert.NotEmpty(rule.Name);
            Assert.NotEmpty(rule.Processes);
            Assert.NotEmpty(rule.Style);
        });
    }
}
