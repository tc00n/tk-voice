using TKVoice.Core.Hotkeys;

namespace TKVoice.Tests.Hotkeys;

public class HotkeyTests
{
    private const int LCtrl = 0xA2, RCtrl = 0xA3, LWin = 0x5B, KeyA = 0x41;

    [Theory]
    [InlineData("RightCtrl")]
    [InlineData("ctrl + win")]
    [InlineData("Ctrl+Shift+F9")]
    [InlineData("Alt+Space")]
    public void Parses_valid_gestures(string text)
    {
        Assert.True(HotkeyGesture.TryParse(text, out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Hyper")]
    [InlineData("+")]
    public void Rejects_invalid_gestures(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Push_to_talk_presses_on_down_and_releases_on_up()
    {
        var matcher = new HotkeyMatcher();
        matcher.Register(HotkeyAction.PushToTalk, HotkeyGesture.Parse("RightCtrl"));

        Assert.Equal([(HotkeyAction.PushToTalk, true)], matcher.OnKey(RCtrl, isDown: true));
        Assert.Empty(matcher.OnKey(RCtrl, isDown: true)); // auto-repeat
        Assert.Equal([(HotkeyAction.PushToTalk, false)], matcher.OnKey(RCtrl, isDown: false));
    }

    [Fact]
    public void Left_ctrl_does_not_trigger_right_ctrl_gesture()
    {
        var matcher = new HotkeyMatcher();
        matcher.Register(HotkeyAction.PushToTalk, HotkeyGesture.Parse("RightCtrl"));

        Assert.Empty(matcher.OnKey(LCtrl, isDown: true));
    }

    [Fact]
    public void Combination_fires_when_all_parts_held_and_releases_when_any_part_released()
    {
        var matcher = new HotkeyMatcher();
        matcher.Register(HotkeyAction.PushToTalk, HotkeyGesture.Parse("Ctrl+Win"));

        Assert.Empty(matcher.OnKey(LCtrl, isDown: true));
        Assert.Equal([(HotkeyAction.PushToTalk, true)], matcher.OnKey(LWin, isDown: true));
        Assert.Empty(matcher.OnKey(KeyA, isDown: true));
        Assert.Equal([(HotkeyAction.PushToTalk, false)], matcher.OnKey(LCtrl, isDown: false));
        Assert.Empty(matcher.OnKey(LWin, isDown: false));
    }

    [Fact]
    public void Generic_modifier_matches_either_side()
    {
        var matcher = new HotkeyMatcher();
        matcher.Register(HotkeyAction.PushToTalk, HotkeyGesture.Parse("Ctrl+Win"));

        matcher.OnKey(RCtrl, isDown: true);
        Assert.Equal([(HotkeyAction.PushToTalk, true)], matcher.OnKey(LWin, isDown: true));
    }
}
