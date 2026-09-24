using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Tapybara.App.Localization;
using Tapybara.Core.Diagnostics;
using Tapybara.Core.Dictation;
using Tapybara.Core.Windows;

namespace Tapybara.App;

/// <summary>Одна диктовка в списке.</summary>
public sealed class DictationRow
{
    public required DictationEntry Entry { get; init; }

    public required string Day { get; init; }

    public string Time => L.S.Time(Entry.At);

    public string Words => string.Format(
        L.S.Formatting,
        L.S.DictationWords,
        Entry.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);

    public string Text => Entry.Text;

    public string CopyHint { get; } = L.S.DictationCopy;

    public string DeleteHint { get; } = L.S.DictationDelete;
}

/// <summary>
/// Диктовки: всё надиктованное, по дням, с поиском.
/// </summary>
/// <remarks>
/// Копирование — главное действие: диктовка, вставленная не туда, нужна
/// в буфере, чтобы вставить её заново. «Вставить» отсюда не предлагаем:
/// фокус сейчас у этого окна, и вставлять было бы некуда.
/// </remarks>
public partial class DictationsPage : System.Windows.Controls.UserControl, IDisposable
{
    private readonly DictationJournal _journal;
    private readonly SettingsHost _settings;
    private readonly Action _openSettings;
    private readonly ObservableCollection<DictationRow> _rows = [];
    private readonly ICollectionView _view;

    public DictationsPage(DictationJournal journal, SettingsHost settings, Action openSettings)
    {
        InitializeComponent();

        _journal = journal;
        _settings = settings;
        _openSettings = openSettings;

        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DictationRow.Day)));
        _view.Filter = Matches;
        EntryList.ItemsSource = _view;

        _journal.Changed += OnJournalChanged;
        _settings.Changed += OnSettingsChanged;

        Reload();
    }

    public void Dispose()
    {
        _journal.Changed -= OnJournalChanged;
        _settings.Changed -= OnSettingsChanged;
        GC.SuppressFinalize(this);
    }

    /// <summary>Подставить надписи текущего языка.</summary>
    public void ApplyLanguage()
    {
        PageTitle.Text = L.S.NavDictations;
        SearchBox.PlaceholderText = L.S.DictationsSearch;
        HistoryOffText.Text = L.S.DictationsHistoryOff;
        HistoryOffButton.Content = L.S.NavSettings;
        Reload();
    }

    private void OnJournalChanged()
    {
        // Журнал правят и из потока интерфейса, и — в будущем — откуда угодно.
        if (Dispatcher.CheckAccess())
        {
            Reload();
        }
        else
        {
            Dispatcher.BeginInvoke(Reload);
        }
    }

    private void OnSettingsChanged(SettingsChange change)
    {
        if (change.Previous.KeepDictationHistory != change.Current.KeepDictationHistory
            || !Equals(change.Previous.Hotkey, change.Current.Hotkey))
        {
            Reload();
        }
    }

    private void Reload()
    {
        _rows.Clear();

        // Сортируем по времени, а не верим порядку строк в файле: после
        // перевода часов или ручной правки журнала «Вчера» вставало над
        // «Сегодня», а группы дней шли в порядке первого появления.
        foreach (DictationEntry entry in _journal.Load().OrderByDescending(e => e.At))
        {
            _rows.Add(new DictationRow { Entry = entry, Day = Day(entry.At) });
        }

        CountText.Text = _rows.Count == 0
            ? string.Empty
            : string.Format(L.S.Formatting, L.S.DictationsCount, _rows.Count);

        HistoryOffBanner.Visibility = _settings.Current.KeepDictationHistory ? Visibility.Collapsed : Visibility.Visible;
        EmptyHint.Text = string.Format(L.S.Formatting, L.S.DictationsEmpty, _settings.Current.Hotkey);
        EmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string Day(DateTimeOffset at)
    {
        DateTime day = at.LocalDateTime.Date;
        DateTime today = DateTime.Today;

        if (day == today)
        {
            return L.S.DayToday;
        }

        if (day == today.AddDays(-1))
        {
            return L.S.DayYesterday;
        }

        return L.S.Date(at);
    }

    private bool Matches(object item)
    {
        string query = SearchBox.Text.Trim();
        return query.Length == 0
               || (item is DictationRow row && row.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => _view.Refresh();

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DictationRow row })
        {
            return;
        }

        try
        {
            // Уважаем ту же настройку, что и при вставке: скопированная
            // заново диктовка — всё та же диктовка, и её не должно быть в
            // истории буфера, если человек так решил.
            ClipboardWriter.SetText(row.Text, _settings.Current.ExcludeFromClipboardHistory);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Не удалось скопировать диктовку.", ex);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DictationRow row })
        {
            _journal.Remove(row.Entry);
        }
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e) => _openSettings();
}
