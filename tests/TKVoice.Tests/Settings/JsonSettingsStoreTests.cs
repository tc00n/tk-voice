using TKVoice.Core.Settings;

namespace TKVoice.Tests.Settings;

public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tkvoice-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Creates_defaults_and_round_trips_changes()
    {
        var store = new JsonSettingsStore(Path.Combine(_directory, "settings.json"));

        var settings = store.LoadOrCreate();
        Assert.True(File.Exists(store.FilePath));
        Assert.Equal("RightCtrl", settings.Hotkeys.PushToTalk);

        settings.Hotkeys.PushToTalk = "Ctrl+Win";
        settings.OpenAI.TranscriptionModel = "some-other-model";
        store.Save(settings);

        var reloaded = store.LoadOrCreate();
        Assert.Equal("Ctrl+Win", reloaded.Hotkeys.PushToTalk);
        Assert.Equal("some-other-model", reloaded.OpenAI.TranscriptionModel);
    }

    [Fact]
    public void Settings_file_contains_no_api_key_field()
    {
        var store = new JsonSettingsStore(Path.Combine(_directory, "settings.json"));
        store.LoadOrCreate();

        Assert.DoesNotContain("apikey", File.ReadAllText(store.FilePath).ToLowerInvariant());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
