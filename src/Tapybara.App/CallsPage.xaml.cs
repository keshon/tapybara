using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Tapybara.App.Localization;
using Tapybara.Core.Audio;
using Tapybara.Core.Calls;
using Tapybara.Core.Diagnostics;
using Tapybara.Core.Settings;
using Tapybara.Core.Windows;
using Wpf.Ui.Controls;

using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using ContextMenu = System.Windows.Controls.ContextMenu;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MenuItem = System.Windows.Controls.MenuItem;
using Orientation = System.Windows.Controls.Orientation;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Tapybara.App;

/// <summary>Что странице звонков нужно от приложения.</summary>
/// <param name="Settings">Настройки: имя владельца, знакомые имена, словарь замен.</param>
/// <param name="CallsDirectory">Где лежат звонки.</param>
/// <param name="LiveState">Что приложение прямо сейчас делает со звонком.</param>
/// <param name="LivePercent">Сколько процентов распознано, если распознаётся.</param>
/// <param name="Transcribe">Распознать звонок.</param>
/// <param name="Reconcile">Привести транскрипт к участникам — перерисовка или разделение заново.</param>
/// <param name="Resplit">Разделить голоса заново на указанное число.</param>
/// <param name="Render">Перерисовать transcript.md. Мгновенно.</param>
/// <param name="Delete">Удалить звонок, отменив работу над ним.</param>
/// <param name="ToggleRecording">Начать или закончить запись звонка.</param>
/// <param name="CanSplitVoices">Есть ли чем разделять голоса.</param>
public sealed record CallsServices(
    SettingsHost Settings,
    Func<string> CallsDirectory,
    Func<string, CallState?> LiveState,
    Func<string, int?> LivePercent,
    Func<CallSession, Task> Transcribe,
    Func<string, Task> Reconcile,
    Func<string, int, Task> Resplit,
    Action<string> Render,
    Action<string> Delete,
    Action ToggleRecording,
    Func<bool> CanSplitVoices);

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

    /// <summary>Группа в списке: «Сегодня», «Вчера», дата.</summary>
    public required string Day { get; init; }

    public required string StateText { get; init; }

    public required Brush StateBackground { get; init; }

    public required Brush StateForeground { get; init; }

    public string Directory => Entry.Directory;

    /// <summary>Всё видимое одной строкой — чтобы не пересобирать список без изменений.</summary>
    public string Signature => $"{Directory}|{Title}|{Subtitle}|{Day}|{StateText}";
}

/// <summary>Одна реплика в транскрипте окна.</summary>
public sealed class TranscriptLineRow : INotifyPropertyChanged
{
    private bool _isCurrent;

    public required CallLine Line { get; init; }

    public required string Stamp { get; init; }

    public required string Speaker { get; init; }

    public required Brush SpeakerBrush { get; init; }

    /// <summary>Неназванный голос — курсивом: это подпись машины, а не имя.</summary>
    public required System.Windows.FontStyle SpeakerFontStyle { get; init; }

    /// <summary>Имя показывается только на смене говорящего, как в transcript.md.</summary>
    public required Visibility SpeakerVisibility { get; init; }

    public required string PlayHint { get; init; }

    public string Text => Line.Text;

    /// <summary>Эту реплику сейчас слышно.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent != value)
            {
                _isCurrent = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Цвета голосов.
/// </summary>
/// <remarks>
/// Средние по светлоте тона: одни и те же читаются и на светлой, и на тёмной
/// теме, и не спорят с красным, который в приложении значит «идёт запись».
/// Цвет у голоса постоянный — по порядку появления, — чтобы «B» был
/// фиолетовым и в списке реплик, и в панели голосов.
/// </remarks>
internal static class VoicePalette
{
    private static readonly Brush[] Voices =
    [
        Frozen(0x1A, 0x9E, 0x8A),
        Frozen(0x8C, 0x5C, 0xF0),
        Frozen(0xE0, 0x7B, 0x24),
        Frozen(0x2F, 0x86, 0xD6),
        Frozen(0xD0, 0x45, 0x8E),
        Frozen(0x6E, 0x9A, 0x2A),
    ];

    /// <summary>Владелец микрофона — коричневый диск со значка приложения.</summary>
    public static Brush Me { get; } = Frozen(0xB0, 0x7A, 0x45);

    /// <summary>Собеседник, голоса которого не разделялись.</summary>
    public static Brush Other { get; } = Frozen(0x7A, 0x7A, 0x80);

    public static Brush For(int index) => Voices[Math.Max(index, 0) % Voices.Length];

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Записи разговоров: список, звонок с транскриптом и голоса.
/// </summary>
/// <remarks>
/// <para>
/// Окно ничего не хранит. Список пересобирается из папки, а правки уходят на
/// диск сразу же: имя голоса — в <c>meta.json</c>, реплика другому голосу —
/// в <c>transcript.json</c>, после чего <c>transcript.md</c> перерисовывается.
/// Второе состояние в памяти означало бы, что закрытое не вовремя окно
/// теряет правки, — а закрывают его как раз не вовремя.
/// </para>
/// <para>
/// Имена голосов даются здесь, по цитатам и на слух. Машина разделяет
/// голоса, но не знает, как их зовут, и раньше раздавала имена по порядку
/// появления — то есть угадывала.
/// </para>
/// </remarks>
public partial class CallsPage : System.Windows.Controls.UserControl, IDisposable
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

    /// <summary>Сколько знакомых имён предлагать чипами у голоса, сверх отмеченных участников.</summary>
    private const int SuggestedNames = 6;

    /// <summary>
    /// Запас вокруг цитаты при прослушивании.
    /// </summary>
    /// <remarks>
    /// Границы реплики Whisper ставит по словам, и первый слог иногда
    /// попадает на долю секунды раньше. Без запаса цитата начиналась бы
    /// с середины слова — а узнают человека как раз по началу фразы.
    /// </remarks>
    private static readonly TimeSpan QuotePadding = TimeSpan.FromMilliseconds(300);

    private readonly CallsServices _services;
    private readonly ObservableCollection<CallRow> _rows = [];
    private readonly ICollectionView _view;
    private readonly ObservableCollection<TranscriptLineRow> _lines = [];
    private readonly DispatcherTimer _refresh;
    private readonly DispatcherTimer _playbackTimer;
    private readonly CallPlayer _player = new();

    /// <summary>Текст транскриптов для поиска: путь → (время правки, текст).</summary>
    private readonly Dictionary<string, (DateTime Stamp, string Text)> _searchCache = new(StringComparer.OrdinalIgnoreCase);

    private CallEntry? _current;
    private CallSession? _session;
    private CallTranscript? _transcript;

    /// <summary>Когда файлы текущего звонка менялись в последний раз — чтобы знать, пора ли перечитать.</summary>
    private string _detailStamp = string.Empty;

    /// <summary>Какая реплика играет как цитата — её кнопка ▶ превращается в ■.</summary>
    private CallLine? _playingQuote;

    private bool _loading;

    /// <summary>Есть ли звонок, ждущий имён, — по последнему просмотру папки.</summary>
    private bool _attention;

    public CallsPage(CallsServices services)
    {
        InitializeComponent();

        _services = services;

        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CallRow.Day)));
        _view.Filter = MatchesSearch;
        CallList.ItemsSource = _view;
        LineList.ItemsSource = _lines;

        NoteBox.LostFocus += (_, _) => SaveNote();

        _refresh = new DispatcherTimer { Interval = RefreshInterval };
        _refresh.Tick += (_, _) => Reload(keepSelection: true);

        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _playbackTimer.Tick += (_, _) => FollowPlayback();
        _player.Stopped += OnPlayerStopped;

        // Страница живёт, пока живёт окно, но на экране — только когда
        // выбрана. Пересматривать папку, пока её не видно, незачем.
        Loaded += (_, _) =>
        {
            Reload(keepSelection: true);

            // Открыли окно — показываем последний звонок, а не пустую панель
            // «выберите запись слева»: чаще всего нужен именно он.
            if (CallList.SelectedItem is null && !_view.IsEmpty)
            {
                CallList.SelectedIndex = 0;
            }

            _refresh.Start();
        };

        Unloaded += (_, _) =>
        {
            _refresh.Stop();
            SaveNote();
            SaveTitle();
        };

        ApplyLanguage();
    }

    private AppSettings Settings => _services.Settings.Current;

    /// <summary>Дописать правки и отпустить устройство вывода. Зовётся при закрытии окна.</summary>
    public void Dispose()
    {
        _refresh.Stop();
        _playbackTimer.Stop();
        SaveNote();
        SaveTitle();
        _player.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Подставить надписи текущего языка.</summary>
    public void ApplyLanguage()
    {
        ListTitle.Text = L.S.SectionCalls;
        SearchBox.PlaceholderText = L.S.CallsSearchPlaceholder;
        EmptyHint.Text = L.S.CallsEmpty;
        NothingSelected.Text = L.S.CallsNothingSelected;
        CopyButton.Content = L.S.CallsCopyText;
        FolderButton.ToolTip = L.S.CallsOpenFolder;
        MoreButton.ToolTip = L.S.CallsMore;
        TranscriptTab.Header = L.S.CallsTabTranscript;
        NoteTab.Header = L.S.CallsTabNote;
        NoteBox.PlaceholderText = L.S.CallReviewNotePlaceholder;
        UpdateListenButton();
        UpdateRecordButton();

        _rows.Clear(); // подписи строк зависят от языка — пересобрать с нуля
        Reload(keepSelection: true);
        ShowDetail(_current, force: true);
    }

    /// <summary>
    /// Появился или пропал звонок, ждущий имён голосов.
    /// </summary>
    /// <remarks>
    /// Главное окно ставит по нему точку на значке раздела: это единственное
    /// состояние звонка, которое ждёт от человека действия, и узнать о нём
    /// надо, не заходя в раздел.
    /// </remarks>
    public event Action<bool>? AttentionChanged;

    /// <summary>Есть ли сейчас звонок, ждущий имён.</summary>
    public bool NeedsAttention => _attention;

    /// <summary>Перечитать список: работа над звонком закончилась.</summary>
    public void RefreshCalls() => Reload(keepSelection: true);

    /// <summary>Выбрать звонок по его папке — например, по щелчку на уведомлении.</summary>
    public void Select(string directory)
    {
        Reload(keepSelection: true);

        CallRow? row = _rows.FirstOrDefault(
            r => string.Equals(r.Directory, directory, StringComparison.OrdinalIgnoreCase));

        if (row is not null)
        {
            SearchBox.Text = string.Empty;
            CallList.SelectedItem = row;
            CallList.ScrollIntoView(row);
        }
    }

    // --- список --------------------------------------------------------------

    private void Reload(bool keepSelection)
    {
        string? selected = keepSelection ? _current?.Directory : null;

        IReadOnlyList<CallEntry> entries = CallLibrary.Scan(_services.CallsDirectory(), _services.LiveState);
        List<CallRow> fresh = [.. entries.Select(ToRow)];

        // Пересобираем список, только если в нём что-то поменялось. Раньше он
        // пересобирался каждые две секунды целиком: сбрасывалась прокрутка,
        // мигала подсветка, а открытое меню у строки закрывалось само.
        if (!fresh.Select(r => r.Signature).SequenceEqual(_rows.Select(r => r.Signature)))
        {
            _loading = true;
            try
            {
                _rows.Clear();
                foreach (CallRow row in fresh)
                {
                    _rows.Add(row);
                }

                CallList.SelectedItem = _rows.FirstOrDefault(
                    r => string.Equals(r.Directory, selected, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                _loading = false;
            }
        }

        bool attention = entries.Any(e => e.State == CallState.NeedsNames);
        if (attention != _attention)
        {
            _attention = attention;
            AttentionChanged?.Invoke(attention);
        }

        EmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TotalsText.Text = _rows.Count == 0
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture,
                L.S.CallsTotals,
                _rows.Count,
                Megabytes(entries.Sum(e => e.AudioBytes)));

        UpdateRecordButton();

        CallRow? current = CallList.SelectedItem as CallRow;
        if (current is null && _current is not null)
        {
            // Выбранное могли удалить из Проводника, пока окно открыто.
            _current = null;
            ShowDetail(null);
            return;
        }

        if (current is not null)
        {
            _current = current.Entry;
            ShowDetail(_current);
        }
    }

    private CallRow ToRow(CallEntry entry)
    {
        CallSession session = entry.Session;
        string time = session.StartedAt.ToString("HH:mm", CultureInfo.CurrentCulture);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(session.Title))
        {
            parts.Add(time);
        }

        parts.Add(L.S.Duration(session.Duration));

        if (!string.IsNullOrWhiteSpace(session.Trigger))
        {
            parts.Add(session.Trigger);
        }

        List<string> people = [.. session.Participants.Concat(session.VoiceNames.Values).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (people.Count > 0)
        {
            parts.Add(string.Join(", ", people));
        }

        (string text, string background, string foreground) = Badge(entry.State);
        if (entry.State == CallState.Transcribing && _services.LivePercent(entry.Directory) is { } percent)
        {
            text = $"{text} · {percent.ToString(CultureInfo.CurrentCulture)}%";
        }

        return new CallRow
        {
            Entry = entry,
            Title = string.IsNullOrWhiteSpace(session.Title) ? DefaultTitle(session) : session.Title,
            Subtitle = string.Join(" · ", parts),
            Day = Day(session.StartedAt),
            StateText = text,
            StateBackground = Resource(background, Colors.Transparent),
            StateForeground = Resource(foreground, Colors.Gray),
        };
    }

    /// <summary>Название звонка, пока человек не дал своё: когда он был.</summary>
    private static string DefaultTitle(CallSession session) =>
        session.StartedAt.ToString("d MMMM, HH:mm", CultureInfo.CurrentCulture);

    /// <summary>Группа списка: «Сегодня», «Вчера» или дата.</summary>
    private static string Day(DateTimeOffset startedAt)
    {
        DateTime day = startedAt.LocalDateTime.Date;
        DateTime today = DateTime.Today;

        if (day == today)
        {
            return L.S.DayToday;
        }

        if (day == today.AddDays(-1))
        {
            return L.S.DayYesterday;
        }

        return day.Year == today.Year
            ? day.ToString("d MMMM", CultureInfo.CurrentCulture)
            : day.ToString("d MMMM yyyy", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Как подписать и покрасить состояние.
    /// </summary>
    /// <remarks>
    /// Цвет здесь смысловой, а не декоративный: «пишется» и «сломано» обязаны
    /// отличаться от «готово» до того, как человек прочитал подпись, а
    /// «назовите голоса» — единственное, что ждёт от него действия. Оттенки
    /// берутся из системной палитры, поэтому они одинаково читаются на
    /// светлой и тёмной теме.
    /// </remarks>
    private static (string Text, string Background, string Foreground) Badge(CallState state) => state switch
    {
        CallState.Recording => (L.S.CallStateRecording, "SystemFillColorCriticalBackgroundBrush", "SystemFillColorCriticalBrush"),
        CallState.Transcribing => (L.S.CallStateTranscribing, "SystemFillColorCautionBackgroundBrush", "SystemFillColorCautionBrush"),
        CallState.Ready => (L.S.CallStateReady, "SystemFillColorSuccessBackgroundBrush", "SystemFillColorSuccessBrush"),
        CallState.NeedsNames => (L.S.CallStateNeedsNames, "SystemFillColorAttentionBackgroundBrush", "AccentTextFillColorPrimaryBrush"),
        CallState.Damaged => (L.S.CallStateDamaged, "SystemFillColorCriticalBackgroundBrush", "SystemFillColorCriticalBrush"),
        _ => (L.S.CallStateNotTranscribed, "SubtleFillColorSecondaryBrush", "TextFillColorSecondaryBrush"),
    };

    private Brush Resource(string key, Color fallback) =>
        TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    private static string Megabytes(long bytes) =>
        (bytes / (1024.0 * 1024.0)).ToString("F0", CultureInfo.CurrentCulture);

    private void UpdateRecordButton()
    {
        bool recording = _rows.Any(r => r.Entry.State == CallState.Recording);
        RecordButton.ToolTip = recording ? L.S.TrayStopRecording : L.S.TrayStartRecording;
        RecordButton.Icon = new SymbolIcon { Symbol = recording ? SymbolRegular.Stop24 : SymbolRegular.Record24 };
    }

    private void OnRecordClick(object sender, RoutedEventArgs e) => _services.ToggleRecording();

    // --- поиск ---------------------------------------------------------------

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => _view.Refresh();

    /// <summary>
    /// Подходит ли звонок под строку поиска.
    /// </summary>
    /// <remarks>
    /// Ищем и по тому, что видно в списке, и по тексту транскрипта: звонок
    /// помнят скорее по тому, о чём говорили, чем по дате. Текст читается с
    /// диска один раз и лежит в кэше, пока файл не поменялся.
    /// </remarks>
    private bool MatchesSearch(object item)
    {
        string query = SearchBox.Text.Trim();
        if (query.Length == 0 || item is not CallRow row)
        {
            return true;
        }

        if (row.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || row.Subtitle.Contains(query, StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        return TranscriptText(row.Entry.Session.TranscriptPath).Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private string TranscriptText(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            DateTime stamp = File.GetLastWriteTimeUtc(path);
            if (_searchCache.TryGetValue(path, out (DateTime Stamp, string Text) cached) && cached.Stamp == stamp)
            {
                return cached.Text;
            }

            string text = File.ReadAllText(path);
            _searchCache[path] = (stamp, text);
            return text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    // --- звонок --------------------------------------------------------------

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return; // пересборка списка, а не выбор пользователя
        }

        // Уходим с записи — недописанные заметка и название не должны пропасть.
        SaveNote();
        SaveTitle();

        _current = (CallList.SelectedItem as CallRow)?.Entry;
        ShowDetail(_current, force: true);
    }

    /// <summary>Отметка «когда что менялось» у файлов звонка.</summary>
    private static string Stamp(CallEntry entry)
    {
        static long Ticks(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0;

        return string.Join(
            '|',
            entry.State,
            Ticks(entry.Session.MetaPath),
            Ticks(entry.Session.TranscriptDataPath),
            Ticks(entry.Session.TranscriptPath));
    }

    /// <summary>Показать звонок. Без <paramref name="force"/> — только если что-то поменялось.</summary>
    private void ShowDetail(CallEntry? entry, bool force = false)
    {
        DetailPane.Visibility = entry is null ? Visibility.Collapsed : Visibility.Visible;
        NothingSelected.Visibility = entry is null ? Visibility.Visible : Visibility.Collapsed;

        if (entry is null)
        {
            _session = null;
            _transcript = null;
            _lines.Clear();
            ShowVoicesPane(false);
            _detailStamp = string.Empty;
            return;
        }

        string stamp = entry.Directory + Stamp(entry) + _services.LivePercent(entry.Directory);
        if (!force && stamp == _detailStamp)
        {
            return;
        }

        bool sameCall = _session is not null
                        && string.Equals(_session.Directory, entry.Directory, StringComparison.OrdinalIgnoreCase);
        _detailStamp = stamp;
        _session = CallMeta.Load(entry.Directory) ?? entry.Session;
        _transcript = entry.HasTranscriptData ? CallTranscriptStore.Load(entry.Directory) : null;

        // Название и заметку не трогаем, пока человек их правит: иначе
        // двухсекундное обновление стирало бы набранное на полуслове.
        if (!sameCall || !TitleBox.IsKeyboardFocused)
        {
            TitleBox.Text = string.IsNullOrWhiteSpace(_session.Title) ? DefaultTitle(_session) : _session.Title;
        }

        if (!sameCall || !NoteBox.IsKeyboardFocused)
        {
            NoteBox.Text = ReadNote(entry.Directory);
        }

        var meta = new List<string>
        {
            _session.StartedAt.ToString("d MMMM yyyy, HH:mm", CultureInfo.CurrentCulture),
            L.S.Duration(_session.Duration),
        };

        if (!string.IsNullOrWhiteSpace(_session.Trigger))
        {
            meta.Add(_session.Trigger);
        }

        meta.Add(string.Format(CultureInfo.CurrentCulture, L.S.CallsMegabytes, Megabytes(entry.AudioBytes)));
        DetailMeta.Text = string.Join(" · ", meta);

        bool hasAudio = entry.State is not (CallState.Damaged or CallState.Recording);
        ListenButton.IsEnabled = hasAudio;
        CopyButton.IsEnabled = File.Exists(_session.TranscriptPath);

        ShowBanner(entry);
        ShowTranscript(entry);
        ShowVoices();
    }

    private void ShowBanner(CallEntry entry)
    {
        BannerProgress.Visibility = Visibility.Collapsed;
        BannerButton.Visibility = Visibility.Collapsed;
        Banner.Background = Resource("SystemFillColorAttentionBackgroundBrush", Colors.Transparent);
        BannerIcon.Symbol = SymbolRegular.Info24;

        string? text = null;
        switch (entry.State)
        {
            case CallState.Recording:
                text = L.S.BannerRecording;
                break;

            case CallState.Transcribing:
                int percent = _services.LivePercent(entry.Directory) ?? 0;
                text = string.Format(CultureInfo.CurrentCulture, L.S.BannerTranscribing, percent);
                BannerProgress.Value = percent;
                BannerProgress.Visibility = Visibility.Visible;
                break;

            case CallState.NotTranscribed:
                text = L.S.BannerNotTranscribed;
                ShowBannerButton(L.S.CallsTranscribe);
                break;

            case CallState.Damaged:
                text = L.S.BannerDamaged;
                Banner.Background = Resource("SystemFillColorCriticalBackgroundBrush", Colors.Transparent);
                break;

            case CallState.NeedsNames when _session is not null:
                int unnamed = _session.Voices.Count(v => CallSpeakers.NameOf(_session, v) is null);
                text = unnamed == 1
                    ? L.S.BannerNeedsNamesOne
                    : string.Format(CultureInfo.CurrentCulture, L.S.BannerNeedsNamesMany, unnamed);
                BannerIcon.Symbol = SymbolRegular.PeopleTeam24;
                break;

            case CallState.Ready when !entry.HasTranscriptData:
                text = L.S.BannerLegacy;
                ShowBannerButton(L.S.CallsTranscribeAgain);
                break;
        }

        BannerText.Text = text ?? string.Empty;
        Banner.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowBannerButton(string text)
    {
        BannerButton.Content = text;
        BannerButton.Visibility = Visibility.Visible;
    }

    private void OnBannerButtonClick(object sender, RoutedEventArgs e) => Transcribe();

    private void ShowTranscript(CallEntry entry)
    {
        bool legacy = _transcript is null && File.Exists(entry.Session.TranscriptPath);
        LegacyScroll.Visibility = legacy ? Visibility.Visible : Visibility.Collapsed;
        LineList.Visibility = legacy ? Visibility.Collapsed : Visibility.Visible;

        if (legacy)
        {
            _lines.Clear();
            LegacyText.Text = TranscriptText(entry.Session.TranscriptPath);
            return;
        }

        LegacyText.Text = string.Empty;
        RebuildLines();
    }

    /// <summary>Разложить реплики в строки окна.</summary>
    /// <remarks>
    /// Подпись говорящего — на смене говорящего, как в transcript.md: Whisper
    /// режет речь на куски по несколько секунд, и подпись на каждом превращала
    /// монолог в столбик одинаковых имён.
    /// </remarks>
    private void RebuildLines()
    {
        TimeSpan? current = _lines.FirstOrDefault(l => l.IsCurrent)?.Line.Start;
        _lines.Clear();

        if (_transcript is null || _session is null)
        {
            return;
        }

        IReadOnlyList<string> voices = _transcript.Voices;
        string myName = Settings.EffectiveMyName;
        string fallback = OtherSideLabel(Settings);
        string? previous = null;

        foreach (CallLine line in _transcript.Lines.OrderBy(l => l.Start))
        {
            string speaker = CallSpeakers.Label(line, _session, voices.Count, myName, fallback, L.S.TranscriptVoice);
            bool named = line.Channel == CallChannel.Mine
                         || line.Voice is null
                         || CallSpeakers.NameOf(_session, line.Voice) is not null
                         || voices.Count <= 1;

            _lines.Add(new TranscriptLineRow
            {
                Line = line,
                Stamp = CallTranscriptRenderer.Stamp(line.Start),
                Speaker = speaker,
                SpeakerBrush = BrushOf(line, voices),
                SpeakerFontStyle = named ? FontStyles.Normal : FontStyles.Italic,
                SpeakerVisibility = speaker == previous ? Visibility.Collapsed : Visibility.Visible,
                PlayHint = string.Format(CultureInfo.CurrentCulture, L.S.LinePlayFrom, CallTranscriptRenderer.Stamp(line.Start)),
                IsCurrent = current == line.Start,
            });

            previous = speaker;
        }
    }

    private static Brush BrushOf(CallLine line, IReadOnlyList<string> voices)
    {
        if (line.Channel == CallChannel.Mine)
        {
            return VoicePalette.Me;
        }

        int index = line.Voice is null ? -1 : IndexOf(voices, line.Voice);
        return index < 0 ? VoicePalette.Other : VoicePalette.For(index);
    }

    private static int IndexOf(IReadOnlyList<string> voices, string voice)
    {
        for (int i = 0; i < voices.Count; i++)
        {
            if (voices[i] == voice)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Как подписан собеседник, когда имён нет.</summary>
    internal static string OtherSideLabel(AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.OtherSideName) ? L.S.TranscriptUnknownSpeaker : settings.OtherSideName;

    // --- название ------------------------------------------------------------

    private void OnTitleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            if (e.Key == Key.Escape && _session is not null)
            {
                TitleBox.Text = string.IsNullOrWhiteSpace(_session.Title) ? DefaultTitle(_session) : _session.Title;
            }

            e.Handled = true;
            Keyboard.ClearFocus();
            LineList.Focus();
        }
    }

    private void OnTitleLostFocus(object sender, KeyboardFocusChangedEventArgs e) => SaveTitle();

    /// <summary>
    /// Записать название звонка.
    /// </summary>
    /// <remarks>
    /// Пока человек своего не дал, в поле стоит дата. Сохранять её как
    /// «название» нельзя: тогда звонок навсегда назывался бы датой на языке,
    /// который был при первом открытии, и не следовал бы за сменой языка.
    /// </remarks>
    private void SaveTitle()
    {
        if (_session is null)
        {
            return;
        }

        string text = TitleBox.Text.Trim();
        string? title = text.Length == 0 || text == DefaultTitle(_session) ? null : text;

        if (title == _session.Title)
        {
            return;
        }

        string directory = _session.Directory;
        _session = CallMeta.Update(directory, s => s with { Title = title }) ?? _session;

        if (_transcript is not null)
        {
            _services.Render(directory);
        }

        Reload(keepSelection: true);
    }

    // --- голоса --------------------------------------------------------------

    private void ShowVoicesPane(bool visible)
    {
        VoicesPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        VoicesColumn.Width = visible ? new GridLength(310) : new GridLength(0);
    }

    /// <summary>
    /// Собрать панель голосов: у каждого — доля речи, цитаты и выбор имени.
    /// </summary>
    /// <remarks>
    /// Карточки собираются кодом, как карточки настроек: их число заранее
    /// неизвестно, а содержимое — чипы имён, цитаты с кнопками — проще
    /// сложить построителем, чем шаблоном с полудюжиной вложенных привязок.
    /// </remarks>
    private void ShowVoices()
    {
        VoicesHost.Children.Clear();

        if (_transcript is null || _session is null || !_transcript.Lines.Any(l => l.Channel == CallChannel.Theirs))
        {
            ShowVoicesPane(false);
            return;
        }

        ShowVoicesPane(true);

        IReadOnlyList<VoiceSummary> voices = CallVoices.Summarize(_transcript);

        var header = new Grid { Margin = new Thickness(2, 0, 2, 10) };
        header.Children.Add(new TextBlock { Text = L.S.VoicesHeader, FontWeight = FontWeights.SemiBold, FontSize = 15 });
        if (voices.Count > 0)
        {
            header.Children.Add(new TextBlock
            {
                Text = string.Format(CultureInfo.CurrentCulture, L.S.VoicesFound, voices.Count),
                FontSize = 12,
                Opacity = 0.6,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        VoicesHost.Children.Add(header);

        if (voices.Count == 0)
        {
            VoicesHost.Children.Add(OtherSideCard());
        }
        else
        {
            for (int i = 0; i < voices.Count; i++)
            {
                VoicesHost.Children.Add(VoiceCard(voices[i], i, voices));
            }
        }

        VoicesHost.Children.Add(MeRow());

        if (_services.CanSplitVoices())
        {
            VoicesHost.Children.Add(ResplitRow());
        }
    }

    /// <summary>Карточка одного голоса.</summary>
    private Border VoiceCard(VoiceSummary voice, int index, IReadOnlyList<VoiceSummary> all)
    {
        string? name = CallSpeakers.NameOf(_session!, voice.Id);
        Brush color = VoicePalette.For(index);
        string label = $"{L.S.TranscriptVoice} {voice.Id}";

        var body = new StackPanel();

        // Заголовок: цвет, имя, доля речи.
        var title = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        title.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 11,
            Height = 11,
            Fill = color,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var nameText = new TextBlock
        {
            Text = name ?? string.Format(CultureInfo.CurrentCulture, L.S.VoiceUnnamed, label),
            FontWeight = FontWeights.SemiBold,
            FontStyle = name is null ? FontStyles.Italic : FontStyles.Normal,
            Opacity = name is null ? 0.75 : 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(nameText, 1);
        title.Children.Add(nameText);

        var stats = new TextBlock
        {
            Text = $"{L.S.Duration(voice.Speech)} · {Math.Round(voice.Share * 100).ToString(CultureInfo.CurrentCulture)}%",
            FontSize = 11.5,
            Opacity = 0.6,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(stats, 2);
        title.Children.Add(stats);
        body.Children.Add(title);

        body.Children.Add(ShareBar(voice.Share, color));

        foreach (CallLine quote in voice.Quotes)
        {
            body.Children.Add(QuoteRow(quote));
        }

        body.Children.Add(new TextBlock
        {
            Text = L.S.VoiceWho,
            FontSize = 12,
            Opacity = 0.7,
            Margin = new Thickness(0, 8, 0, 6),
        });

        body.Children.Add(NameChips(name, picked => SetVoiceName(voice.Id, picked)));

        if (all.Count > 1)
        {
            var merge = new Button
            {
                Content = L.S.VoiceSameAs,
                Style = (Style)FindResource("FlatLinkButton"),
                Foreground = Resource("AccentTextFillColorPrimaryBrush", Colors.SteelBlue),
                FontSize = 12,
                Margin = new Thickness(-2, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            merge.Click += (_, _) =>
            {
                var menu = new ContextMenu { PlacementTarget = merge };
                foreach (VoiceSummary other in all.Where(v => v.Id != voice.Id))
                {
                    string otherName = CallSpeakers.NameOf(_session!, other.Id) ?? $"{L.S.TranscriptVoice} {other.Id}";
                    var item = new MenuItem { Header = otherName };
                    item.Click += (_, _) => MergeVoices(voice.Id, other.Id);
                    menu.Items.Add(item);
                }

                menu.IsOpen = true;
            };

            body.Children.Add(merge);
        }

        bool needsName = name is null && all.Count > 1;
        return new Border
        {
            Child = body,
            Padding = new Thickness(12, 10, 12, 12),
            Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(8),
            Background = Resource("CardBackgroundFillColorDefaultBrush", Colors.White),
            BorderBrush = needsName
                ? Resource("AccentFillColorDefaultBrush", Colors.SteelBlue)
                : Resource("CardStrokeColorDefaultBrush", Colors.LightGray),
            BorderThickness = new Thickness(needsName ? 1.5 : 1),
        };
    }

    /// <summary>
    /// Карточка собеседника, когда голоса не разделялись.
    /// </summary>
    /// <remarks>
    /// Имя здесь становится единственным участником звонка — и подписывает
    /// весь чужой канал. Это то же правило, что и у окна после звонка:
    /// один собеседник — сопоставлять нечего.
    /// </remarks>
    private Border OtherSideCard()
    {
        string? name = _session!.Participants.Count == 1 ? _session.Participants[0] : null;

        var body = new StackPanel();
        var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        title.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 11,
            Height = 11,
            Fill = VoicePalette.Other,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        title.Children.Add(new TextBlock { Text = name ?? L.S.VoiceTheOtherSide, FontWeight = FontWeights.SemiBold });
        body.Children.Add(title);

        body.Children.Add(new TextBlock
        {
            Text = L.S.VoiceWho,
            FontSize = 12,
            Opacity = 0.7,
            Margin = new Thickness(0, 0, 0, 6),
        });

        body.Children.Add(NameChips(name, SetOtherSideName));

        return new Border
        {
            Child = body,
            Padding = new Thickness(12, 10, 12, 12),
            Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(8),
            Background = Resource("CardBackgroundFillColorDefaultBrush", Colors.White),
            BorderBrush = Resource("CardStrokeColorDefaultBrush", Colors.LightGray),
            BorderThickness = new Thickness(1),
        };
    }

    private static Grid ShareBar(double share, Brush color)
    {
        var bar = new Grid { Height = 4, Margin = new Thickness(0, 0, 0, 8) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(share, 0.001), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - share, 0.001), GridUnitType.Star) });

        var track = new Border
        {
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0x80, 0x80, 0x80)),
        };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = color });
        return bar;
    }

    /// <summary>Цитата с кнопкой прослушивания.</summary>
    private Grid QuoteRow(CallLine quote)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        bool playing = _playingQuote == quote && _player.IsPlaying;
        var play = new Wpf.Ui.Controls.Button
        {
            Icon = new SymbolIcon { Symbol = playing ? SymbolRegular.Stop16 : SymbolRegular.Play16 },
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            CornerRadius = new CornerRadius(12),
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = L.S.VoicePlayQuote,
        };
        play.Click += (_, _) => PlayQuote(quote);
        row.Children.Add(play);

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = $"«{quote.Text}»",
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
        });
        text.Children.Add(new TextBlock
        {
            Text = CallTranscriptRenderer.Stamp(quote.Start),
            FontSize = 11,
            Opacity = 0.55,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    /// <summary>
    /// Чипы имён: кто подходит на этот голос.
    /// </summary>
    /// <remarks>
    /// Сначала отмеченные на карточке после звонка, потом знакомые — свежие
    /// первыми. Искать нужное имя среди шестидесяти незачем: на звонке были
    /// те, кого отметили, а если нет — «другое…» открывает поле.
    /// </remarks>
    private WrapPanel NameChips(string? current, Action<string?> pick)
    {
        var chips = new WrapPanel();
        Style style = (Style)FindResource("ParticipantChipStyle");

        List<string> names = [.. _session!.Participants];
        foreach (string known in _session.VoiceNames.Values.Concat(Settings.KnownParticipants))
        {
            if (names.Count >= _session.Participants.Count + SuggestedNames)
            {
                break;
            }

            if (!names.Contains(known, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(known);
            }
        }

        if (current is not null && !names.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            names.Insert(0, current);
        }

        foreach (string name in names)
        {
            bool selected = string.Equals(name, current, StringComparison.OrdinalIgnoreCase);
            var chip = new ToggleButton
            {
                Content = name,
                IsChecked = selected,
                Style = style,
                Padding = new Thickness(10, 3, 10, 3),
                FontSize = 12.5,
            };
            // Checked/Unchecked, а не Click: щелчок — лишь один из способов
            // переключить чип. Экранный диктор и UI Automation переключают
            // его через TogglePattern, и Click при этом не приходит вовсе.
            chip.Checked += (_, _) => pick(name);
            chip.Unchecked += (_, _) => pick(null);
            chips.Children.Add(chip);
        }

        var other = new ToggleButton
        {
            Content = L.S.VoiceOtherName,
            Style = style,
            Padding = new Thickness(10, 3, 10, 3),
            FontSize = 12.5,
            Opacity = 0.8,
        };

        other.Checked += (_, _) =>
        {
            int at = chips.Children.IndexOf(other);
            chips.Children.Remove(other);

            var entry = new Wpf.Ui.Controls.TextBox
            {
                PlaceholderText = L.S.VoiceNamePlaceholder,
                Width = 140,
                Margin = new Thickness(0, 0, 6, 6),
            };

            entry.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    string? typed = KnownParticipants.Normalize(Settings.KnownParticipants, entry.Text);
                    if (typed is not null)
                    {
                        pick(typed);
                    }
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    ShowVoices();
                }
            };

            chips.Children.Insert(at, entry);
            entry.Focus();
        };

        chips.Children.Add(other);
        return chips;
    }

    private Border MeRow()
    {
        TimeSpan mine = TimeSpan.FromSeconds(_transcript!.Lines
            .Where(l => l.Channel == CallChannel.Mine)
            .Sum(l => Math.Max(0, (l.End - l.Start).TotalSeconds)));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 4, 0, 0) };
        row.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = VoicePalette.Me,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = string.Format(CultureInfo.CurrentCulture, L.S.VoiceMe, Settings.EffectiveMyName, L.S.Duration(mine)),
            FontSize = 12.5,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
        });

        return new Border { Child = row };
    }

    /// <summary>
    /// «Разделено неверно? Искать голосов: 2 3 4 5».
    /// </summary>
    /// <remarks>
    /// Без подсказки разделитель ошибается в числе голосов чаще, чем в их
    /// границах: склеивает двух похожих или разваливает одного простуженного.
    /// Сказать ему число — самый сильный рычаг, и стоит это минуту
    /// процессора, а не повторное распознавание.
    /// </remarks>
    private StackPanel ResplitRow()
    {
        var panel = new StackPanel { Margin = new Thickness(2, 16, 0, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = L.S.VoicesResplit,
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        });

        var chips = new WrapPanel();
        Style style = (Style)FindResource("ParticipantChipStyle");
        int found = _transcript!.Voices.Count;

        for (int count = 2; count <= 5; count++)
        {
            int wanted = count;
            var chip = new ToggleButton
            {
                Content = wanted.ToString(CultureInfo.CurrentCulture),
                IsChecked = wanted == found,
                Style = style,
                Padding = new Thickness(12, 3, 12, 3),
                FontSize = 12.5,
            };

            chip.Click += (_, _) =>
            {
                chip.IsChecked = wanted == found;
                if (wanted != found && _session is not null)
                {
                    _ = _services.Resplit(_session.Directory, wanted);
                    Reload(keepSelection: true);
                }
            };

            chips.Children.Add(chip);
        }

        panel.Children.Add(chips);
        return panel;
    }

    /// <summary>
    /// Дать голосу имя — или забрать.
    /// </summary>
    /// <remarks>
    /// Одно имя — один голос: названный «Кириллом» второй голос отнимает имя у
    /// первого. Два голоса с одним именем — это не «Кирилл говорил двумя
    /// голосами», а ошибка, которую человек сделал, промахнувшись чипом;
    /// склеить голоса — отдельное действие «это тот же человек».
    /// </remarks>
    private void SetVoiceName(string voice, string? name)
    {
        if (_session is null)
        {
            return;
        }

        string directory = _session.Directory;
        _session = CallMeta.Update(directory, s =>
        {
            var names = new Dictionary<string, string>(s.VoiceNames);
            foreach (string taken in names.Where(p => name is not null
                                                      && p.Key != voice
                                                      && string.Equals(p.Value, name, StringComparison.OrdinalIgnoreCase))
                                          .Select(p => p.Key)
                                          .ToList())
            {
                names.Remove(taken);
            }

            if (name is null)
            {
                names.Remove(voice);
            }
            else
            {
                names[voice] = name;
            }

            return s with { VoiceNames = names };
        }) ?? _session;

        RememberName(name);
        _services.Render(directory);
        RefreshAfterEdit();
    }

    private void SetOtherSideName(string? name)
    {
        if (_session is null)
        {
            return;
        }

        string directory = _session.Directory;
        _session = CallMeta.Update(directory, s => s with { Participants = name is null ? [] : [name] }) ?? _session;

        RememberName(name);
        _ = _services.Reconcile(directory);
        RefreshAfterEdit();
    }

    /// <summary>Поднять имя в начало знакомых: на следующем звонке оно будет первым чипом.</summary>
    private void RememberName(string? name)
    {
        if (name is not null)
        {
            _services.Settings.Update(s => s with
            {
                KnownParticipants = [.. KnownParticipants.Touch(s.KnownParticipants, [name])],
            });
        }
    }

    private void MergeVoices(string from, string into)
    {
        if (_session is null)
        {
            return;
        }

        string directory = _session.Directory;
        CallTranscript? merged = CallTranscriptStore.Update(directory, t => CallVoices.Merge(t, from, into));
        if (merged is null)
        {
            return;
        }

        _session = CallMeta.Update(directory, s =>
        {
            var names = new Dictionary<string, string>(s.VoiceNames);
            if (names.TryGetValue(from, out string? carried) && !names.ContainsKey(into))
            {
                names[into] = carried;
            }

            names.Remove(from);
            return s with { Voices = merged.Voices, VoiceNames = names };
        }) ?? _session;

        _services.Render(directory);
        RefreshAfterEdit();
    }

    /// <summary>Отдать реплику другому голосу.</summary>
    private void ReassignLine(CallLine line, string voice)
    {
        if (_session is null)
        {
            return;
        }

        string directory = _session.Directory;
        CallTranscript? changed = CallTranscriptStore.Update(directory, t => CallVoices.Reassign(t, line, voice));
        if (changed is null)
        {
            return;
        }

        _session = CallMeta.Update(directory, s => s with { Voices = changed.Voices }) ?? _session;
        _services.Render(directory);
        RefreshAfterEdit();
    }

    /// <summary>Перечитать звонок после правки — список, транскрипт и голоса.</summary>
    private void RefreshAfterEdit()
    {
        Reload(keepSelection: true);
        if (_current is not null)
        {
            // Переоткрываем по свежей записи из списка: состояние («назовите
            // голоса» → «готово») вычисляется там.
            ShowDetail(_current, force: true);
        }
    }

    // --- реплика -------------------------------------------------------------

    private void OnSpeakerClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TranscriptLineRow row } element)
        {
            ContextMenu menu = LineMenu(row);
            menu.PlacementTarget = element;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnLineContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is TextBox { Tag: TranscriptLineRow row } box)
        {
            box.ContextMenu = LineMenu(row, box);
        }
    }

    /// <summary>
    /// Меню реплики: кто её сказал, послушать, скопировать, добавить замену.
    /// </summary>
    /// <remarks>
    /// Переназначить можно только реплику собеседника и только другому
    /// голосу собеседника. Своя дорожка — это микрофон владельца по
    /// построению; реплика, ошибочно на ней оказавшаяся, — работа фильтра
    /// эха, а не разделения голосов.
    /// </remarks>
    private ContextMenu LineMenu(TranscriptLineRow row, TextBox? box = null)
    {
        var menu = new ContextMenu();

        if (row.Line.Channel == CallChannel.Theirs && _transcript is { } transcript && transcript.Voices.Count > 1)
        {
            menu.Items.Add(new MenuItem { Header = L.S.LineSaidBy, IsEnabled = false });
            foreach (string voice in transcript.Voices)
            {
                string label = CallSpeakers.NameOf(_session!, voice) ?? $"{L.S.TranscriptVoice} {voice}";
                var item = new MenuItem
                {
                    Header = label,
                    IsCheckable = true,
                    IsChecked = row.Line.Voice == voice,
                    Icon = new System.Windows.Shapes.Ellipse
                    {
                        Width = 9,
                        Height = 9,
                        Fill = VoicePalette.For(IndexOf(transcript.Voices, voice)),
                    },
                };

                string target = voice;
                item.Click += (_, _) =>
                {
                    if (row.Line.Voice != target)
                    {
                        ReassignLine(row.Line, target);
                    }
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
        }
        else if (row.Line.Channel == CallChannel.Mine)
        {
            menu.Items.Add(new MenuItem { Header = L.S.LineYourMicrophone, IsEnabled = false });
            menu.Items.Add(new Separator());
        }

        var play = new MenuItem { Header = row.PlayHint, Icon = new SymbolIcon { Symbol = SymbolRegular.Play24 } };
        play.Click += (_, _) => PlayFrom(row.Line.Start);
        menu.Items.Add(play);

        var copy = new MenuItem { Header = L.S.LineCopy, Icon = new SymbolIcon { Symbol = SymbolRegular.Copy24 } };
        copy.Click += (_, _) =>
            Copy(box is { SelectionLength: > 0 } ? box.SelectedText : $"{row.Speaker}: {row.Text}");
        menu.Items.Add(copy);

        var replace = new MenuItem { Header = L.S.LineAddReplacement, Icon = new SymbolIcon { Symbol = SymbolRegular.TextEditStyle24 } };
        replace.Click += (_, _) => AddReplacement(box is { SelectionLength: > 0 } ? box.SelectedText : string.Empty);
        menu.Items.Add(replace);

        return menu;
    }

    /// <summary>
    /// Добавить замену в словарь прямо из транскрипта.
    /// </summary>
    /// <remarks>
    /// Именно здесь человек и видит, что имя коллеги снова расслышано не так.
    /// Раньше для этого надо было запомнить слово, открыть настройки, найти
    /// раздел «Текст» и дописать строку в формате «услышано = правильно».
    /// </remarks>
    private void AddReplacement(string heard)
    {
        var heardBox = new Wpf.Ui.Controls.TextBox { Text = heard.Trim(), PlaceholderText = L.S.ReplacementHeard };
        var correctBox = new Wpf.Ui.Controls.TextBox { PlaceholderText = L.S.ReplacementCorrect };

        var dialog = new ConfirmWindow(
            L.S.ReplacementTitle,
            L.S.ReplacementHint,
            primaryButton: L.S.ButtonAdd,
            cancelButton: L.S.ButtonCancel,
            icon: SymbolRegular.TextEditStyle24);

        dialog.AddContent(Labeled(L.S.ReplacementHeard, heardBox));
        dialog.AddContent(Labeled(L.S.ReplacementCorrect, correctBox));
        dialog.Loaded += (_, _) => (heard.Length == 0 ? heardBox : correctBox).Focus();

        if (ConfirmWindow.Ask(Window.GetWindow(this), dialog) != ConfirmChoice.Primary)
        {
            return;
        }

        string from = heardBox.Text.Trim();
        string to = correctBox.Text.Trim();
        if (from.Length == 0 || to.Length == 0)
        {
            return;
        }

        _services.Settings.Update(s => s with
        {
            Replacements = new Dictionary<string, string>(s.Replacements) { [from] = to },
        });
    }

    private static StackPanel Labeled(string label, UIElement field)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.65, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(field);
        return panel;
    }

    // --- прослушивание -------------------------------------------------------

    private void OnListenClick(object sender, RoutedEventArgs e)
    {
        if (_player.IsPlaying)
        {
            _player.Stop();
            return;
        }

        TimeSpan from = _lines.FirstOrDefault(l => l.IsCurrent)?.Line.Start ?? TimeSpan.Zero;
        PlayFrom(from);
    }

    private void OnStampClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TranscriptLineRow row })
        {
            PlayFrom(row.Line.Start);
        }
    }

    private void PlayFrom(TimeSpan from)
    {
        if (_current is null)
        {
            return;
        }

        _playingQuote = null;
        if (_player.Play(_current.Directory, from))
        {
            _playbackTimer.Start();
            FollowPlayback();
        }

        UpdateListenButton();
    }

    /// <summary>Послушать цитату голоса — только его дорожку, только эту реплику.</summary>
    private void PlayQuote(CallLine quote)
    {
        if (_current is null)
        {
            return;
        }

        if (_playingQuote == quote && _player.IsPlaying)
        {
            _player.Stop();
            return;
        }

        TimeSpan from = quote.Start - QuotePadding;
        bool started = _player.Play(
            _current.Directory,
            from < TimeSpan.Zero ? TimeSpan.Zero : from,
            quote.End + QuotePadding,
            CallChannel.Theirs);

        _playingQuote = started ? quote : null;
        if (started)
        {
            _playbackTimer.Start();
            FollowPlayback();
        }

        UpdateListenButton();
        ShowVoices();
    }

    /// <summary>Подсветить реплику, которую сейчас слышно.</summary>
    private void FollowPlayback()
    {
        if (!_player.IsPlaying || _current is null
            || !string.Equals(_player.CallDirectory, _current.Directory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        TimeSpan position = _player.Position;
        TranscriptLineRow? now = null;
        foreach (TranscriptLineRow row in _lines)
        {
            if (row.Line.Start > position)
            {
                break;
            }

            // Играет одна дорожка — подсвечиваем только её реплики.
            if (_player.Channel is null || row.Line.Channel == _player.Channel)
            {
                now = row;
            }
        }

        foreach (TranscriptLineRow row in _lines)
        {
            row.IsCurrent = row == now;
        }
    }

    private void OnPlayerStopped()
    {
        _playbackTimer.Stop();
        bool wasQuote = _playingQuote is not null;
        _playingQuote = null;
        UpdateListenButton();

        if (wasQuote)
        {
            ShowVoices();
        }
    }

    private void UpdateListenButton()
    {
        bool playing = _player.IsPlaying && _playingQuote is null;
        ListenButton.Content = playing ? L.S.CallsStopListening : L.S.CallsListen;
        ListenButton.Icon = new SymbolIcon { Symbol = playing ? SymbolRegular.Stop24 : SymbolRegular.Play24 };
    }

    // --- действия ------------------------------------------------------------

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (_session is not null)
        {
            Copy(TranscriptText(_session.TranscriptPath));
        }
    }

    private static void Copy(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            // В историю буфера — можно: это не диктовка, а текст, который
            // человек сам решил скопировать.
            ClipboardWriter.SetText(text, excludeFromHistory: false);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Не удалось скопировать транскрипт.", ex);
        }
    }

    private void OnFolderClick(object sender, RoutedEventArgs e)
    {
        if (_current is not null)
        {
            Open(_current.Directory);
        }
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = MoreButton, Placement = PlacementMode.Bottom };
        bool busy = _current.State is CallState.Recording or CallState.Transcribing;

        var again = new MenuItem
        {
            Header = _current.State == CallState.NotTranscribed ? L.S.CallsTranscribe : L.S.CallsTranscribeAgain,
            Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowClockwise24 },
            IsEnabled = !busy && _current.State != CallState.Damaged,
        };
        again.Click += (_, _) => Transcribe();
        menu.Items.Add(again);

        var file = new MenuItem
        {
            Header = L.S.CallsOpenTranscriptFile,
            Icon = new SymbolIcon { Symbol = SymbolRegular.DocumentText24 },
            IsEnabled = File.Exists(_current.Session.TranscriptPath),
        };
        file.Click += (_, _) => Open(_current.Session.TranscriptPath);
        menu.Items.Add(file);

        menu.Items.Add(new Separator());

        var delete = new MenuItem
        {
            Header = L.S.CallReviewDelete,
            Icon = new SymbolIcon { Symbol = SymbolRegular.Delete24 },
            IsEnabled = _current.State != CallState.Recording,
        };
        delete.Click += (_, _) => DeleteCurrent();
        menu.Items.Add(delete);

        menu.IsOpen = true;
    }

    private void Transcribe()
    {
        if (_current is null)
        {
            return;
        }

        CallSession session = CallMeta.Load(_current.Directory) ?? _current.Session;
        _ = _services.Transcribe(session);
        Reload(keepSelection: true);
    }

    private void DeleteCurrent()
    {
        if (_current is null)
        {
            return;
        }

        ConfirmChoice choice = ConfirmWindow.Ask(Window.GetWindow(this), new ConfirmWindow(
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
        if (string.Equals(_player.CallDirectory, directory, StringComparison.OrdinalIgnoreCase))
        {
            _player.Stop();
        }

        _current = null;
        ShowDetail(null);
        _services.Delete(directory);
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
        if (_session is null)
        {
            return;
        }

        string directory = _session.Directory;
        string path = Path.Combine(directory, CallLibrary.NoteFileName);
        string text = NoteBox.Text.Trim();

        try
        {
            if (!System.IO.Directory.Exists(directory))
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

            if (string.Equals(ReadNote(directory).Trim(), text, StringComparison.Ordinal))
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
