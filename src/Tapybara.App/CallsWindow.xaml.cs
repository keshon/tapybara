using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Tapybara.Core.Diagnostics;
using Tapybara.Core.Settings;
using Wpf.Ui.Controls;

using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace Tapybara.App;

/// <summary>Строка списка звонков — то, что видит глаз.</summary>
/// <remarks>
/// Отдельный тип, а не привязка прямо к <see cref="CallEntry"/>: список
/// показывает не поля, а уже сложенные из них фразы («12:04 · Zoom ·
/// Кирилл, Марина»), и собирать их в разметке значило бы разложить логику
/// по XAML, где её не видно и не проверить.
/// </remarks>
public sealed class CallRow
{
    public required CallEntry Entry { get; init; }

    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required string StateText { get; init; }

    public required Brush StateBackground { get; init; }

    public required Brush StateForeground { get; init; }

    public string Directory => Entry.Directory;
}

/// <summary>
/// Записи разговоров: что записано, что распознано, кто в них был.
/// </summary>
/// <remarks>
/// Окно ничего не хранит. Список пересобирается из папки, а правки участников
/// и заметки уходят на диск сразу же. Второе состояние в памяти означало бы,
/// что закрытое не вовремя окно теряет правки, — а закрывают его как раз не
/// вовремя.
/// </remarks>
public partial class CallsWindow : FluentWindow
{
    /// <summary>
    /// Как часто пересматриваем папку, пока окно открыто.
    /// </summary>
    /// <remarks>
    /// Распознавание идёт в фоне и меняет состояние записи без нашего участия.
    /// Две секунды — незаметно для диска и достаточно, чтобы «распознаётся»
    /// сменилось на «готово» на глазах.
    /// </remarks>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly SettingsHost _host;
    private readonly Func<string> _callsDirectory;
    private readonly Func<string, CallState?> _liveState;
    private readonly Func<CallSession, Task> _transcribe;

    private readonly ObservableCollection<CallRow> _rows = [];
    private readonly DispatcherTimer _refresh;

    private CallEntry? _current;
    private bool _loading;

    public CallsWindow(
        SettingsHost host,
        Func<string> callsDirectory,
        Func<string, CallState?> liveState,
        Func<CallSession, Task> transcribe)
    {
        InitializeComponent();

        _host = host;
        _callsDirectory = callsDirectory;
        _liveState = liveState;
        _transcribe = transcribe;

        CallList.ItemsSource = _rows;
        Participants.SelectionChanged += OnParticipantsChanged;
        NoteBox.LostFocus += (_, _) => SaveNote();

        _refresh = new DispatcherTimer { Interval = RefreshInterval };
        _refresh.Tick += (_, _) => Reload(keepSelection: true);

        Loaded += (_, _) =>
        {
            Reload(keepSelection: false);
            _refresh.Start();
        };

        Closing += (_, _) =>
        {
            _refresh.Stop();
            SaveNote();
        };

        ApplyLanguage();
    }

    /// <summary>Подставить надписи текущего языка.</summary>
    public void ApplyLanguage()
    {
        Title = L.S.CallsTitle;
        WindowTitleBar.Title = L.S.CallsTitle;
        EmptyHint.Text = L.S.CallsEmpty;
        NothingSelected.Text = L.S.CallsNothingSelected;
        ParticipantsLabel.Text = L.S.CallReviewParticipants;
        NoteLabel.Text = L.S.CallReviewNote;
        NoteBox.PlaceholderText = L.S.CallReviewNotePlaceholder;
        DeleteButton.Content = L.S.CallReviewDelete;
        FolderButton.Content = L.S.CallsOpenFolder;
        OpenTranscriptButton.Content = L.S.CallsOpenTranscript;

        Participants.ApplyLanguage();
        Reload(keepSelection: true);
    }

    // --- список --------------------------------------------------------------

    private void Reload(bool keepSelection)
    {
        string? selected = keepSelection ? _current?.Directory : null;

        IReadOnlyList<CallEntry> entries = CallLibrary.Scan(_callsDirectory(), _liveState);

        _loading = true;
        try
        {
            _rows.Clear();
            foreach (CallEntry entry in entries)
            {
                _rows.Add(ToRow(entry));
            }

            CallList.SelectedItem = _rows.FirstOrDefault(
                r => string.Equals(r.Directory, selected, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _loading = false;
        }

        EmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TotalsText.Text = _rows.Count == 0
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture,
                L.S.CallsTotals,
                _rows.Count,
                Megabytes(entries.Sum(e => e.AudioBytes)));

        // Выбранное могли удалить из Проводника, пока окно открыто.
        if (CallList.SelectedItem is null && _current is not null)
        {
            _current = null;
            ShowDetail(null);
        }
    }

    private CallRow ToRow(CallEntry entry)
    {
        var parts = new List<string> { L.S.Duration(entry.Session.Duration) };

        if (!string.IsNullOrWhiteSpace(entry.Session.Trigger))
        {
            parts.Add(entry.Session.Trigger);
        }

        if (entry.Session.Participants.Count > 0)
        {
            parts.Add(string.Join(", ", entry.Session.Participants));
        }

        (string text, string background, string foreground) = Badge(entry.State);

        return new CallRow
        {
            Entry = entry,
            Title = entry.Session.StartedAt.ToString("d MMMM, HH:mm", CultureInfo.CurrentCulture),
            Subtitle = string.Join(" · ", parts),
            StateText = text,
            StateBackground = Resource(background, Colors.Transparent),
            StateForeground = Resource(foreground, Colors.Gray),
        };
    }

    /// <summary>
    /// Как подписать и покрасить состояние.
    /// </summary>
    /// <remarks>
    /// Цвет здесь смысловой, а не декоративный: «пишется» и «сломано» обязаны
    /// отличаться от «готово» до того, как человек прочитал подпись. Оттенки
    /// берутся из системной палитры, поэтому они одинаково читаются на светлой
    /// и тёмной теме.
    /// </remarks>
    private static (string Text, string Background, string Foreground) Badge(CallState state) => state switch
    {
        CallState.Recording => (L.S.CallStateRecording, "SystemFillColorCriticalBackgroundBrush", "SystemFillColorCriticalBrush"),
        CallState.Transcribing => (L.S.CallStateTranscribing, "SystemFillColorCautionBackgroundBrush", "SystemFillColorCautionBrush"),
        CallState.Ready => (L.S.CallStateReady, "SystemFillColorSuccessBackgroundBrush", "SystemFillColorSuccessBrush"),
        CallState.Damaged => (L.S.CallStateDamaged, "SystemFillColorCriticalBackgroundBrush", "SystemFillColorCriticalBrush"),
        _ => (L.S.CallStateNotTranscribed, "SubtleFillColorSecondaryBrush", "TextFillColorSecondaryBrush"),
    };

    private Brush Resource(string key, Color fallback) =>
        TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    private static string Megabytes(long bytes) =>
        (bytes / (1024.0 * 1024.0)).ToString("F0", CultureInfo.CurrentCulture);

    // --- подробности ---------------------------------------------------------

    private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return; // пересборка списка, а не выбор пользователя
        }

        SaveNote(); // уходим с записи — недописанная заметка не должна пропасть
        _current = (CallList.SelectedItem as CallRow)?.Entry;
        ShowDetail(_current);
    }

    private void ShowDetail(CallEntry? entry)
    {
        DetailPane.Visibility = entry is null ? Visibility.Collapsed : Visibility.Visible;
        NothingSelected.Visibility = entry is null ? Visibility.Visible : Visibility.Collapsed;

        if (entry is null)
        {
            return;
        }

        AppSettings settings = _host.Current;

        DetailTitle.Text = entry.Session.StartedAt.ToString("d MMMM yyyy, HH:mm", CultureInfo.CurrentCulture);

        var meta = new List<string>
        {
            L.S.Duration(entry.Session.Duration),
            string.Format(CultureInfo.CurrentCulture, L.S.CallsMegabytes, Megabytes(entry.AudioBytes)),
        };

        if (!string.IsNullOrWhiteSpace(entry.Session.Trigger))
        {
            meta.Insert(1, entry.Session.Trigger);
        }

        DetailMeta.Text = string.Join(" · ", meta);

        Participants.Load(settings.EffectiveMyName, settings.KnownParticipants, entry.Session.Participants);
        UpdateParticipantsHint();

        NoteBox.Text = ReadNote(entry.Directory);

        bool ready = entry.State == CallState.Ready;
        bool busy = entry.State is CallState.Recording or CallState.Transcribing;

        OpenTranscriptButton.IsEnabled = ready;
        TranscribeButton.IsEnabled = !busy && entry.State != CallState.Damaged;
        TranscribeButton.Content = ready ? L.S.CallsTranscribeAgain : L.S.CallsTranscribe;
        DeleteButton.IsEnabled = entry.State != CallState.Recording;
    }

    private void UpdateParticipantsHint() =>
        ParticipantsHint.Text = Participants.Selected.Count switch
        {
            0 => L.S.CallReviewHintNone,
            1 => L.S.CallReviewHintOne,
            _ => string.Format(CultureInfo.CurrentCulture, L.S.CallReviewHintMany, Participants.Selected.Count),
        };

    /// <summary>
    /// Имена правятся на месте и сохраняются сразу.
    /// </summary>
    /// <remarks>
    /// Без кнопки «Сохранить»: щелчок по чипу — уже законченное действие, и
    /// требовать после него подтверждения значило бы сделать из одного клика
    /// два. Транскрипт при этом не пересобирается: имена попадут в него при
    /// следующем распознавании, и кнопка для этого рядом.
    /// </remarks>
    private void OnParticipantsChanged()
    {
        if (_current is null)
        {
            return;
        }

        IReadOnlyList<string> selected = Participants.Selected;
        CallMeta.Save(_current.Session with { Participants = selected });

        if (selected.Count > 0)
        {
            _host.Update(s => s with
            {
                KnownParticipants = [.. KnownParticipants.Touch(s.KnownParticipants, selected)],
            });
        }

        _current = _current with { Session = _current.Session with { Participants = selected } };
        UpdateParticipantsHint();
        Reload(keepSelection: true);
    }

    // --- действия ------------------------------------------------------------

    private void OnOpenTranscriptClick(object sender, RoutedEventArgs e)
    {
        if (_current is not null)
        {
            Open(_current.Session.TranscriptPath);
        }
    }

    private void OnFolderClick(object sender, RoutedEventArgs e)
    {
        if (_current is not null)
        {
            Open(_current.Directory);
        }
    }

    private async void OnTranscribeClick(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            return;
        }

        Participants.Flush();
        CallSession session = _current.Session with { Participants = Participants.Selected };

        TranscribeButton.IsEnabled = false;
        try
        {
            await _transcribe(session);
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось распознать звонок из списка.", ex);
        }
        finally
        {
            Reload(keepSelection: true);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            return;
        }

        ConfirmChoice choice = ConfirmWindow.Ask(this, new ConfirmWindow(
            L.S.CallDeleteTitle,
            L.S.CallDeleteBody,
            primaryButton: L.S.CallDeleteConfirm,
            cancelButton: L.S.ButtonCancel,
            icon: SymbolRegular.Delete24,
            danger: true));

        if (choice != ConfirmChoice.Primary)
        {
            return;
        }

        string directory = _current.Directory;
        _current = null;
        ShowDetail(null);

        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                directory,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось удалить папку звонка.", ex);
        }

        Reload(keepSelection: false);
    }

    private static void Open(string path)
    {
        try
        {
            if (!File.Exists(path) && !System.IO.Directory.Exists(path))
            {
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Не удалось открыть {path}.", ex);
        }
    }

    // --- заметка -------------------------------------------------------------

    private static string ReadNote(string directory)
    {
        string path = Path.Combine(directory, CallLibrary.NoteFileName);
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private void SaveNote()
    {
        if (_current is null)
        {
            return;
        }

        string path = Path.Combine(_current.Directory, CallLibrary.NoteFileName);
        string text = NoteBox.Text.Trim();

        try
        {
            if (!System.IO.Directory.Exists(_current.Directory))
            {
                return; // папку унесли, пока окно было открыто
            }

            if (text.Length == 0)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }

            if (string.Equals(ReadNote(_current.Directory).Trim(), text, StringComparison.Ordinal))
            {
                return; // ничего не изменилось — не трогаем время правки файла
            }

            File.WriteAllText(path, text + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Не удалось сохранить заметку о звонке.", ex);
        }
    }
}
