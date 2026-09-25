using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TKVoice.Core.Dictionary;

namespace TKVoice.App.Settings;

/// <summary>View, add, edit and delete personal dictionary entries. Changes are saved immediately.</summary>
public partial class DictionaryEditor : UserControl
{
    private PersonalDictionary? _dictionary;
    private string? _editing;

    public DictionaryEditor()
    {
        InitializeComponent();

        // Tab switches unload and reload the page; subscribe only while it is shown.
        Loaded += (_, _) =>
        {
            if (_dictionary is not null)
            {
                _dictionary.Changed += OnDictionaryChanged;
                Refresh();
            }
        };
        Unloaded += (_, _) =>
        {
            if (_dictionary is not null)
            {
                _dictionary.Changed -= OnDictionaryChanged;
            }
        };
    }

    internal void Bind(PersonalDictionary dictionary) => _dictionary = dictionary;

    private void OnDictionaryChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        var selected = TermList.SelectedItem as string;
        var terms = _dictionary!.Terms.OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase).ToList();
        TermList.ItemsSource = terms;
        TermList.SelectedItem = selected is not null && terms.Contains(selected) ? selected : null;
        CountText.Text = terms.Count == 1 ? "1 Eintrag" : $"{terms.Count} Einträge";
    }

    private void OnTermBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnSave(sender, e);
            e.Handled = true;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!PersonalDictionary.TryNormalize(TermBox.Text, out var term, out var error))
        {
            ShowError(error == "Kein Begriff markiert." ? "Bitte einen Begriff eingeben." : error);
            return;
        }

        var saved = _editing is null ? _dictionary!.Add(term) : _dictionary!.Update(_editing, term);
        if (!saved)
        {
            ShowError($"„{term}“ ist bereits im Wörterbuch.");
            return;
        }

        EndEdit();
        TermList.SelectedItem = term;
        TermList.ScrollIntoView(term);
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (TermList.SelectedItem is not string term)
        {
            return;
        }

        _editing = term;
        TermBox.Text = term;
        TermBox.SelectAll();
        TermBox.Focus();
        SaveButton.Content = "Übernehmen";
        CancelEditButton.Visibility = Visibility.Visible;
        ShowError(null);
    }

    private void OnCancelEdit(object sender, RoutedEventArgs e) => EndEdit();

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (TermList.SelectedItem is string term)
        {
            _dictionary!.Remove(term);
            if (_editing == term)
            {
                EndEdit();
            }
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = TermList.SelectedItem is not null;
        EditButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
    }

    private void EndEdit()
    {
        _editing = null;
        TermBox.Clear();
        SaveButton.Content = "Hinzufügen";
        CancelEditButton.Visibility = Visibility.Collapsed;
        ShowError(null);
        TermBox.Focus();
    }

    private void ShowError(string? message)
    {
        ErrorText.Text = message ?? string.Empty;
        ErrorText.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
