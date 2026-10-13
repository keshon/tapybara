using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Wpf.Ui.Controls;
using ComboBox = System.Windows.Controls.ComboBox;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Tapybara.App;

/// <summary>Фильтры и локальные действия списка — всегда по захваченной записи, а не по текущей.</summary>
public partial class CallsPage
{
    private sealed record StateChoice(string Label, CallState? State);
    private readonly ComboBox _peopleFilter = new();
    private readonly ComboBox _stateFilter = new() { DisplayMemberPath = nameof(StateChoice.Label) };
    private readonly Controls.DateRangeFilter _dates = new();
    private DatePicker _dateFrom => _dates.From;
    private DatePicker _dateTo => _dates.To;
    private bool _updatingFilters;
    private bool _reviewVisible;

    private void InitializeLibraryFilters()
    {
        string? person = _peopleFilter.SelectedIndex > 0 ? _peopleFilter.SelectedItem as string : null;
        CallState? stateSelection = (_stateFilter.SelectedItem as StateChoice)?.State;
        _updatingFilters = true;
        if (LibrarySearch.Filters.Content is StackPanel previous)
        {
            previous.Children.Clear();
        }
        var content = new StackPanel();
        _peopleFilter.Items.Clear();
        _peopleFilter.Items.Add(L.S.LibraryAllPeople);
        if (person is not null)
        {
            _peopleFilter.Items.Add(person);
        }
        _peopleFilter.SelectedItem = person ?? L.S.LibraryAllPeople;
        _stateFilter.Items.Clear();
        _stateFilter.Items.Add(new StateChoice(L.S.LibraryAllStates, null));
        foreach (CallState state in Enum.GetValues<CallState>())
        {
            _stateFilter.Items.Add(new StateChoice(Badge(state).Text, state));
        }
        _stateFilter.SelectedItem = _stateFilter.Items.Cast<StateChoice>().First(c => c.State == stateSelection);
        foreach (FrameworkElement control in new FrameworkElement[] { _peopleFilter, _stateFilter })
        {
            if (control is System.Windows.Controls.Control sized) { Controls.ControlSizing.Apply(sized); }
            control.Margin = new Thickness(0, 0, 0, Tokens.Space2);
            content.Children.Add(control);
        }
        _dates.SetLabels(L.S.LibraryFromDate, L.S.LibraryToDate);
        content.Children.Add(_dates);
        var reset = new Wpf.Ui.Controls.Button { Content = L.S.LibraryResetFilters,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, Tokens.Space2, 0, 0) };
        Controls.ControlSizing.Apply(reset);
        reset.Click += (_, _) => ClearLibraryFilters();
        content.Children.Add(reset);
        AutomationProperties.SetName(_peopleFilter, L.S.LibraryAllPeople);
        AutomationProperties.SetName(_stateFilter, L.S.LibraryAllStates);
        AutomationProperties.SetName(_dateFrom, L.S.LibraryFromDate);
        AutomationProperties.SetName(_dateTo, L.S.LibraryToDate);
        LibrarySearch.Filters.Content = content;
        _peopleFilter.SelectionChanged -= OnFilterChanged;
        _peopleFilter.SelectionChanged += OnFilterChanged;
        _stateFilter.SelectionChanged -= OnFilterChanged;
        _stateFilter.SelectionChanged += OnFilterChanged;
        _dateFrom.SelectedDateChanged -= OnFilterChanged;
        _dateFrom.SelectedDateChanged += OnFilterChanged;
        _dateTo.SelectedDateChanged -= OnFilterChanged;
        _dateTo.SelectedDateChanged += OnFilterChanged;
        _updatingFilters = false;
        RefreshFilterChips();
    }

    private void UpdateFilterPeople(IReadOnlyList<CallEntry> entries)
    {
        string? selected = _peopleFilter.SelectedIndex > 0 ? _peopleFilter.SelectedItem as string : null;
        List<string> names = [.. entries.SelectMany(e => e.Session.Participants.Concat(e.Session.VoiceNames.Values))
            .Where(n => n != CallSession.Me).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)];
        if (selected is not null && !names.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            names.Add(selected);
        }
        if (_peopleFilter.Items.Cast<string>().Skip(1).SequenceEqual(names))
        {
            return;
        }
        _updatingFilters = true;
        _peopleFilter.Items.Clear();
        _peopleFilter.Items.Add(L.S.LibraryAllPeople);
        foreach (string name in names)
        {
            _peopleFilter.Items.Add(name);
        }
        _peopleFilter.SelectedItem = selected ?? L.S.LibraryAllPeople;
        _updatingFilters = false;
    }

    private CallLibraryFilter LibraryFilter() => new(Person: _peopleFilter.SelectedIndex > 0 ? _peopleFilter.SelectedItem as string : null,
        State: (_stateFilter.SelectedItem as StateChoice)?.State,
        From: _dateFrom.SelectedDate is { } from ? DateOnly.FromDateTime(from) : null,
        To: _dateTo.SelectedDate is { } to ? DateOnly.FromDateTime(to) : null);

    private void OnFilterChanged(object? sender, EventArgs e)
    {
        if (_updatingFilters)
        {
            return;
        }
        RefreshFilterChips();
        RequestLibraryFilter();
    }

    private void RefreshFilterChips()
    {
        List<(string Text, Action Remove)> chips = [];
        CallLibraryFilter filter = LibraryFilter();
        if (filter.Person is { } person)
        {
            chips.Add((person, () => _peopleFilter.SelectedIndex = 0));
        }
        if (filter.State is { } state)
        {
            chips.Add((Badge(state).Text, () => _stateFilter.SelectedIndex = 0));
        }
        if (_dateFrom.SelectedDate is { } from)
        {
            chips.Add((L.S.LibraryFromDate + ": " + from.ToShortDateString(), () => _dateFrom.SelectedDate = null));
        }
        if (_dateTo.SelectedDate is { } to)
        {
            chips.Add((L.S.LibraryToDate + ": " + to.ToShortDateString(), () => _dateTo.SelectedDate = null));
        }
        LibrarySearch.SetActiveFilters(chips);
    }

    private void ClearLibraryFilters()
    {
        _updatingFilters = true;
        _peopleFilter.SelectedIndex = 0;
        _stateFilter.SelectedIndex = 0;
        _dateFrom.SelectedDate = null;
        _dateTo.SelectedDate = null;
        _updatingFilters = false;
        RefreshFilterChips();
        RequestLibraryFilter();
    }

    private void RequestLibraryFilter()
    {
        if (_disposed)
        {
            return;
        }
        _filterCancellation?.Cancel();
        _filterCancellation?.Dispose();
        _filterCancellation = new CancellationTokenSource();
        int version = ++_filterVersion;
        CancellationToken token = _filterCancellation.Token;
        CallLibraryFilter filter = LibraryFilter() with { Query = SearchBox.Text };
        IReadOnlyList<CallLibraryItem> snapshot = [.. _libraryItems.Values];
        _ = ApplyLibraryFilterAsync(version, snapshot, filter, token);
    }

    private async Task ApplyLibraryFilterAsync(int version, IReadOnlyList<CallLibraryItem> snapshot,
        CallLibraryFilter filter, CancellationToken cancellationToken)
    {
        try
        {
            HashSet<string> matches = await Task.Run(() =>
            {
                var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (CallLibraryItem item in snapshot)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (filter.Matches(item))
                    {
                        result.Add(item.Entry.Directory);
                    }
                }
                return result;
            }, cancellationToken);
            if (version != _filterVersion || _disposed)
            {
                return;
            }
            // Одинаковый фильтр не должен каждые две секунды пересоздавать
            // контейнеры групп и лишать открытое меню его PlacementTarget.
            bool changed = !_matchingDirectories.SetEquals(matches);
            _matchingDirectories = matches;
            if (changed) { _view.Refresh(); }
            UpdateLibraryEmpty();
            if (_requestedDirectory is not null && matches.Contains(_requestedDirectory))
            {
                CallList.SelectedItem = _rows.FirstOrDefault(r => r.Directory.Equals(_requestedDirectory, StringComparison.OrdinalIgnoreCase));
                _requestedDirectory = null;
                CallList.ScrollIntoView(CallList.SelectedItem);
            }
            else if (CallList.SelectedItem is null && _current is null && !_view.IsEmpty && IsLoaded)
            {
                CallList.SelectedIndex = 0;
            }
        }
        catch (OperationCanceledException) { }
    }

    private void UpdateLibraryEmpty()
    {
        EmptyHint.Visibility = _view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Text = _rows.Count == 0 ? L.S.CallsEmpty : L.S.LibraryNoMatches;
    }

    private void OnCallRowContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CallRow row } anchor)
        {
            Controls.ContextMenuBehavior.Open(anchor, e, CallActions(row.Entry));
        }
    }

    private void OnCallRowMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CallRow row } anchor)
        {
            ContextMenu menu = CallActions(row.Entry);
            Tapybara.App.Controls.AnchoredMenu.Open(menu, anchor);
        }
    }

    private ContextMenu CallActions(CallEntry entry)
    {
        var menu = new ContextMenu();
        void Add(string label, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = label, IsEnabled = enabled };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add(L.S.CallsRename, () => RenameCall(entry));
        Add(L.S.CallsExport, () => OpenExport(entry), entry.HasTranscriptData || System.IO.File.Exists(entry.Session.TranscriptPath));
        Add(L.S.CallsOpenFolder, () => Open(entry.Directory));
        Add(L.S.CallsOpenTranscriptFile, () => Open(entry.Session.TranscriptPath), System.IO.File.Exists(entry.Session.TranscriptPath));
        menu.Items.Add(new Separator());
        bool busy = entry.State is CallState.Recording or CallState.Transcribing;
        Add(entry.State == CallState.NotTranscribed ? L.S.CallsTranscribe : L.S.CallsTranscribeAgain,
            () => TranscribeCall(entry), !busy && entry.State != CallState.Damaged);
        if (entry.State == CallState.Transcribing)
        {
            Add(L.S.CallsStopTranscribing, () => _services.Cancel(entry.Directory));
        }
        menu.Items.Add(new Separator());
        Add(L.S.CallReviewDelete, () => DeleteCall(entry), entry.State != CallState.Recording);
        return menu;
    }

    private void RenameCall(CallEntry entry)
    {
        var name = new Wpf.Ui.Controls.TextBox { Text = TitleOf(entry.Session), ClearButtonEnabled = false };
        var window = new ConfirmWindow(L.S.CallsRename.TrimEnd('…', '.'), string.Empty, L.S.EditApply, L.S.ButtonCancel);
        window.AddContent(name);
        if (ConfirmWindow.Ask(Window.GetWindow(this), window) == ConfirmChoice.Primary && !string.IsNullOrWhiteSpace(name.Text))
        {
            CallMeta.Update(entry.Directory, s => s with { Title = name.Text.Trim() });
            _services.Render(entry.Directory);
            Reload(keepSelection: true);
        }
    }

    private void TranscribeCall(CallEntry entry) => _ = TranscribeCallAsync(entry);

    private async Task TranscribeCallAsync(CallEntry entry)
    {
        if (_services.LiveState(entry.Directory) is CallState.Recording or CallState.Transcribing)
        {
            return;
        }
        if (_activeEditor?.Flush() == false)
        {
            return;
        }
        if (CallTranscriptStore.Load(entry.Directory) is { EditedByHand: true }
            && ConfirmWindow.Ask(Window.GetWindow(this), new ConfirmWindow(L.S.RetranscribeEditedTitle, L.S.RetranscribeEditedBody,
                L.S.CallsTranscribeAgain, L.S.ButtonCancel, icon: SymbolRegular.Warning24)) != ConfirmChoice.Primary)
        {
            return;
        }
        if (_player.CallDirectory?.Equals(entry.Directory, StringComparison.OrdinalIgnoreCase) == true)
        {
            _player.Stop();
        }
        await SettleTimelineBeforeProcessing(entry.Directory);
        if (_disposed || !System.IO.Directory.Exists(entry.Directory)
            || _services.LiveState(entry.Directory) is CallState.Recording or CallState.Transcribing) { return; }
        _ = _services.Transcribe(CallMeta.Load(entry.Directory) ?? entry.Session);
        Reload(keepSelection: true);
    }

    private void DeleteCall(CallEntry entry)
    {
        if (_services.LiveState(entry.Directory) == CallState.Recording)
        {
            return;
        }
        if (_activeEditor?.Flush() == false || ConfirmWindow.Ask(Window.GetWindow(this), new ConfirmWindow(
            L.S.CallDeleteTitle, L.S.CallDeleteBody, L.S.CallDeleteConfirm, L.S.ButtonCancel, icon: SymbolRegular.Delete24, danger: true)) != ConfirmChoice.Primary)
        {
            return;
        }
        if (_player.CallDirectory?.Equals(entry.Directory, StringComparison.OrdinalIgnoreCase) == true)
        {
            _player.Stop();
        }
        if (_current?.Directory.Equals(entry.Directory, StringComparison.OrdinalIgnoreCase) == true)
        {
            _current = null;
            ShowDetail(null);
        }
        _services.Delete(entry.Directory);
        Reload(keepSelection: true);
    }

    private void OnReviewClick(object sender, RoutedEventArgs e)
    {
        _reviewVisible = !_reviewVisible;
        _suggestions = _reviewVisible ? Suggest() : [];
        ShowVoices();
    }
}
