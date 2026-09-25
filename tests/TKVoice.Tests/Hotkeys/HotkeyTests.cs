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

    [Theory]
    [InlineData(new[] { "Space", "RightCtrl" }, "RightCtrl+Space")]
    [InlineData(new[] { "F12", "Shift", "Ctrl" }, "Ctrl+Shift+F12")]
    [InlineData(new[] { "Win", "Alt", "A" }, "Alt+Win+A")]
    [InlineData(new[] { "RightCtrl" }, "RightCtrl")]
    public void Format_orders_modifiers_first(string[] keys, string expected)
    {
        Assert.Equal(expected, HotkeyGesture.Format(keys));
        Assert.True(HotkeyGesture.TryParse(expected, out _, out _));
    }

    [Fact]
    public void Format_ignores_unknown_keys()
    {
        Assert.Null(HotkeyGesture.Format(["Hyper"]));
    }

    [Fact]
    public void Equivalent_gestures_are_detected_regardless_of_order_and_case()
    {
        Assert.True(HotkeyGesture.Parse("Ctrl+Shift+F12").IsEquivalentTo(HotkeyGesture.Parse("shift+ctrl+f12")));
        Assert.False(HotkeyGesture.Parse("Ctrl+F12").IsEquivalentTo(HotkeyGesture.Parse("RightCtrl+F12")));
        Assert.False(HotkeyGesture.Parse("RightCtrl").IsEquivalentTo(HotkeyGesture.Parse("RightCtrl+Space")));
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
