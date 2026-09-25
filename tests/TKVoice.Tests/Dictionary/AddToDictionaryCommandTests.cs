using TKVoice.Core.Abstractions;
using TKVoice.Core.Dictionary;

namespace TKVoice.Tests.Dictionary;

public class AddToDictionaryCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tkvoice-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeSelection _selection = new();
    private readonly RecordingNotifier _notifier = new();
    private readonly PersonalDictionary _dictionary;

    public AddToDictionaryCommandTests()
    {
        _dictionary = new PersonalDictionary(Path.Combine(_directory, "dictionary.json"));
    }

    private async Task ExecuteAsync()
    {
        var command = new AddToDictionaryCommand(_selection, _dictionary, _notifier, new NullLog());
        command.Execute();
        await command.Completion;
    }

    [Fact]
    public async Task Adds_selected_term_and_confirms()
    {
        _selection.Text = " NEONEX ";

        await ExecuteAsync();

        Assert.Equal(["NEONEX"], _dictionary.Terms);
        Assert.Contains("NEONEX", _notifier.Infos.Single());
    }

    [Fact]
    public async Task Existing_term_is_reported_not_duplicated()
    {
        _dictionary.Add("NEONEX");
        _selection.Text = "neonex";

        await ExecuteAsync();

        Assert.Single(_dictionary.Terms);
        Assert.Contains("bereits", _notifier.Infos.Single());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Ein ganzer Absatz\nmit mehreren Zeilen")]
    public async Task Missing_or_invalid_selection_shows_error(string? selection)
    {
        _selection.Text = selection;

        await ExecuteAsync();

        Assert.Empty(_dictionary.Terms);
        Assert.Single(_notifier.Errors);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeSelection : ISelectionReader
    {
        public string? Text { get; set; }

        public Task<string?> ReadSelectedTextAsync(CancellationToken cancellationToken) => Task.FromResult(Text);
    }

    private sealed class RecordingNotifier : IUserNotifier
    {
        public List<string> Infos { get; } = [];
        public List<string> Errors { get; } = [];

        public void SetState(DictationState state)
        {
        }

        public void ShowError(string message) => Errors.Add(message);

        public void ShowInfo(string message) => Infos.Add(message);
    }
}
