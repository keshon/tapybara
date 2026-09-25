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
/// <param name="Verify">Сверить реплики с голосами у звонка, который ещё не сверяли.</param>
/// <param name="RefreshPrints">Снять слепки голосов заново после ручной правки.</param>
/// <param name="Render">Перерисовать transcript.md. Мгновенно.</param>
/// <param name="Delete">Удалить звонок, отменив работу над ним.</param>
/// <param name="Cancel">Остановить распознавание звонка или убрать его из очереди.</param>
/// <param name="ToggleRecording">Начать или закончить запись звонка.</param>
/// <param name="CanSplitVoices">Есть ли чем разделять голоса.</param>
/// <param name="FetchModel">Скачать недостающую модель этого типа.</param>
/// <param name="Voices">Книга голосов — для подсказок «похоже на…» и чтобы запоминать названные.</param>
/// <param name="Live">Что сейчас пишется — для кнопки записи.</param>
public sealed record CallsServices(
    SettingsHost Settings,
    Func<string> CallsDirectory,
    Func<string, CallState?> LiveState,
    Func<string, int?> LivePercent,
    Func<CallSession, Task> Transcribe,
    Func<string, Task> Reconcile,
    Func<string, int, Task> Resplit,
    Func<string, Task> Verify,
    Func<string, Task> RefreshPrints,
    Action<string> Render,
    Action<string> Delete,
    Action<string> Cancel,
    Action ToggleRecording,
    Func<bool> CanSplitVoices,
    Action<Tapybara.Core.Models.ModelKind> FetchModel,
    VoiceBook Voices,
    LiveActivity Live);

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

    /// <summary>
    /// Значок виден только у состояний, которые что-то значат.
    /// </summary>
    /// <remarks>
    /// «Готово» — обычное состояние звонка. Девятнадцать зелёных «готово»
    /// подряд были шумом, среди которого терялись «назовите голоса» и
    /// «распознаётся» — ради которых значок и заведён.
    /// </remarks>
    public Visibility BadgeVisibility =>
        Entry.State == CallState.Ready ? Visibility.Collapsed : Visibility.Visible;

    public string Directory => Entry.Directory;

    /// <summary>Всё видимое одной строкой — чтобы не пересобирать список без изменений.</summary>
    public string Signature => $"{Directory}|{Title}|{Subtitle}|{Day}|{StateText}";
}

/// <summary>Одна реплика в транскрипте окна.</summary>
public sealed class TranscriptLineRow : INotifyPropertyChanged
{
    private bool _isCurrent;
    private bool _isPlaying;
    private double _progress;

    public required CallLine Line { get; init; }

    public required string Stamp { get; init; }

    public required string Speaker { get; init; }

    public required Brush SpeakerBrush { get; init; }

    /// <summary>Неназванный голос — курсивом: это подпись машины, а не имя.</summary>
    public required System.Windows.FontStyle SpeakerFontStyle { get; init; }

    /// <summary>Имя показывается только на смене говорящего, как в transcript.md.</summary>
    public required Visibility SpeakerVisibility { get; init; }

    /// <summary>Цвет имени: основной текст, у неназванного голоса — вторичный.</summary>
    public required Brush SpeakerForeground { get; init; }

    /// <summary>Воздух перед сменой говорящего: реплики одного человека идут плотнее.</summary>
    public Thickness Spacing => SpeakerVisibility == Visibility.Visible
        ? new Thickness(0, Tokens.Space2, 0, 0)
        : new Thickness(0);

    public required string PlayHint { get; init; }

    /// <summary>Голос реплики под сомнением (см. <see cref="CallLine.IsDoubtful"/>).</summary>
    public Visibility DoubtVisibility => Line.IsDoubtful ? Visibility.Visible : Visibility.Collapsed;

    public string DoubtHint { get; } = L.S.LineDoubtful;

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

    /// <summary>Эту реплику слышно прямо сейчас: у времени ■ вместо ▶.</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying != value)
            {
                _isPlaying = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
            }
        }
    }

    /// <summary>Сколько реплики уже прозвучало, 0–1: столько строки и залито.</summary>
    public double Progress
    {
        get => _progress;
        set
        {
            if (Math.Abs(_progress - value) > 0.001)
            {
                _progress = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
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
    private readonly RecordPill _record = new(call: true)
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };
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

    /// <summary>Подсказки книги голосов для неназванных голосов этого звонка: голос → кто похож.</summary>
    private Dictionary<string, VoiceMatch> _suggestions = [];

    /// <summary>Что делает кнопка в баннере — зависит от того, о чём баннер.</summary>
    private Action? _bannerAction;

    private bool _loading;

    /// <summary>Есть ли звонок, ждущий имён, — по последнему просмотру папки.</summary>
    private bool _attention;

    public CallsPage(CallsServices services)
    {
        InitializeComponent();

        _services = services;

        _record.Click += (_, _) => _services.ToggleRecording();
        RecordHost.Content = _record;
        _services.Live.Changed += UpdateRecordButton;

        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CallRow.Day)));
        _view.Filter = MatchesSearch;
        CallList.ItemsSource = _view;
        LineList.ItemsSource = _lines;

        NoteBox.LostFocus += (_, _) => SaveNote();

        _refresh = new DispatcherTimer { Interval = RefreshInterval };
        _refresh.Tick += (_, _) => Reload(keepSelection: true);

        // Десять раз в секунду: заливка строки должна бежать, а не шагать.
        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
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
        _services.Live.Changed -= UpdateRecordButton;
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
        TranscriptTab.Content = L.S.CallsTabTranscript;
        NoteTab.Content = L.S.CallsTabNote;
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
                L.S.Formatting,
                L.S.CallsTotals,
                _rows.Count,
                L.S.Size(entries.Sum(e => e.AudioBytes)));

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
        string title = TitleOf(session);

        // В подписи — то, чего нет в названии. Раньше дата стояла трижды:
        // в заголовке группы, в названии строки и ещё раз в карточке.
        var parts = new List<string> { L.S.Time(session.StartedAt), L.S.Duration(session.Duration) };

        if (!string.IsNullOrWhiteSpace(session.Trigger) && session.Trigger != title)
        {
            parts.Add(session.Trigger);
        }

        string people = PeopleOf(session);
        if (people.Length > 0 && people != title)
        {
            parts.Add(people);
        }

        (string text, string background, string foreground) = Badge(entry.State);
        if (entry.State == CallState.Transcribing && _services.LivePercent(entry.Directory) is { } percent)
        {
            text = $"{text} · {percent.ToString(L.S.Formatting)}%";
        }

        return new CallRow
        {
            Entry = entry,
            Title = title,
            Subtitle = string.Join(" · ", parts),
            Day = Day(session.StartedAt),
            StateText = text,
            StateBackground = Resource(background, Colors.Transparent),
            StateForeground = Resource(foreground, Colors.Gray),
        };
    }

    /// <summary>Кто был на звонке — отмеченные и названные голоса.</summary>
    private static string PeopleOf(CallSession session) =>
        string.Join(", ", session.Participants
            .Concat(session.VoiceNames.Values.Where(n => n != CallSession.Me))
            .Distinct(StringComparer.OrdinalIgnoreCase));

    /// <summary>Как называется звонок.</summary>
    private static string TitleOf(CallSession session) =>
        string.IsNullOrWhiteSpace(session.Title) ? DefaultTitle(session) : session.Title;

    /// <summary>
    /// Название звонка, пока человек не дал своё.
    /// </summary>
    /// <remarks>
    /// Люди, а не дата: дата уже есть в заголовке группы, а «Костя, Витя»
    /// отличает звонок от соседних с первого взгляда. Нет людей — приложение,
    /// из которого звонили; нет и его — просто «Звонок».
    /// </remarks>
    private static string DefaultTitle(CallSession session)
    {
        string people = PeopleOf(session);
        if (people.Length > 0)
        {
            return people;
        }

        return string.IsNullOrWhiteSpace(session.Trigger) ? L.S.CallUntitled : session.Trigger;
    }

    /// <summary>Группа списка: «Сегодня», «Вчера» или дата.</summary>
    private static string Day(DateTimeOffset startedAt)
    {
        DateTime day = startedAt.LocalDateTime.Date;
        DateTime today = DateTime.Today;

        if (day == today)
        {
            return L.S.DayToday;
        }

        return day == today.AddDays(-1) ? L.S.DayYesterday : L.S.Date(startedAt);
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

    private void UpdateRecordButton()
    {
        LiveActivity live = _services.Live;
        if (live.CallRecording)
        {
            _record.ShowRecording(L.S.RecordStop, live.CallElapsed);
            _record.ToolTip = L.S.TrayStopRecording;
        }
        else
        {
            _record.ShowIdle(L.S.RecordCall, Settings.CallHotkey.ToString());
            _record.ToolTip = null;
        }
    }

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

        if (!sameCall)
        {
            _rejected.Clear();
        }

        // Звонок распознан до сверки реплик с голосами — сверяем один раз, в
        // фоне. Без этого старые звонки так и показывали бы чужие цитаты.
        if (_transcript is { VoicesChecked: false }
            && entry.State is CallState.Ready or CallState.NeedsNames
            && _transcript.Lines.Any(l => l.Channel == CallChannel.Theirs)
            && _verifyAsked.Add(entry.Directory))
        {
            _ = _services.Verify(entry.Directory);
        }

        // Название и заметку не трогаем, пока человек их правит: иначе
        // двухсекундное обновление стирало бы набранное на полуслове.
        if (!sameCall || !TitleBox.IsKeyboardFocused)
        {
            TitleBox.Text = TitleOf(_session);
        }

        if (!sameCall || !NoteBox.IsKeyboardFocused)
        {
            NoteBox.Text = ReadNote(entry.Directory);
        }

        var meta = new List<string>
        {
            L.S.Date(_session.StartedAt, withYear: true, withTime: true),
            L.S.Duration(_session.Duration),
        };

        if (!string.IsNullOrWhiteSpace(_session.Trigger))
        {
            meta.Add(_session.Trigger);
        }

        meta.Add(L.S.Size(entry.AudioBytes));
        DetailMeta.Text = string.Join(" · ", meta);

        bool hasAudio = entry.State is not (CallState.Damaged or CallState.Recording);
        ListenButton.IsEnabled = hasAudio;
        CopyButton.IsEnabled = File.Exists(_session.TranscriptPath);

        _suggestions = Suggest();
        ShowBanner(entry);
        ShowTranscript(entry);
        ShowVoices();
    }

    private void ShowBanner(CallEntry entry)
    {
        BannerProgress.Visibility = Visibility.Collapsed;
        BannerButton.Visibility = Visibility.Collapsed;
        _bannerAction = null;
        Banner.Background = Resource("SubtleFillColorSecondaryBrush", Colors.Transparent);
        BannerIcon.Symbol = SymbolRegular.Info24;

        string? text = null;
        switch (entry.State)
        {
            case CallState.Recording:
                text = L.S.BannerRecording;
                break;

            case CallState.Transcribing:
                int percent = _services.LivePercent(entry.Directory) ?? 0;
                text = string.Format(L.S.Formatting, L.S.BannerTranscribing, percent);
                BannerProgress.Value = percent;
                BannerProgress.Visibility = Visibility.Visible;

                // Распознавание идёт минутами, и запущенное по ошибке раньше
                // приходилось ждать до конца.
                string transcribing = entry.Directory;
                ShowBannerButton(L.S.CallsStopTranscribing, () => _services.Cancel(transcribing));
                break;

            case CallState.NotTranscribed:
                text = L.S.BannerNotTranscribed;
                ShowBannerButton(L.S.CallsTranscribe, Transcribe);
                break;

            case CallState.Damaged:
                text = L.S.BannerDamaged;
                Banner.Background = Resource("SystemFillColorCriticalBackgroundBrush", Colors.Transparent);
                break;

            case CallState.NeedsNames when _session is not null && _suggestions.Count > 0:
                // Книга голосов кого-то узнала — предлагаем принять одной
                // кнопкой, но не принимаем сами: подсказка остаётся подсказкой.
                text = string.Format(
                    L.S.Formatting,
                    L.S.BannerSuggested,
                    string.Join(", ", _suggestions.Select(s => $"{L.S.TranscriptVoice} {s.Key} — {s.Value.Name}")));
                BannerIcon.Symbol = SymbolRegular.PersonVoice24;
                ShowBannerButton(L.S.BannerAcceptSuggestions, AcceptSuggestions);
                break;

            case CallState.NeedsNames when _session is not null:
                int unnamed = _session.Voices.Count(v => CallSpeakers.NameOf(_session, v) is null);
                text = unnamed == 1
                    ? L.S.BannerNeedsNamesOne
                    : string.Format(L.S.Formatting, L.S.BannerNeedsNamesMany, unnamed);
                BannerIcon.Symbol = SymbolRegular.PeopleTeam24;
                break;

            case CallState.Ready when !entry.HasTranscriptData:
                text = L.S.BannerLegacy;
                ShowBannerButton(L.S.CallsTranscribeAgain, Transcribe);
                break;
        }

        BannerText.Text = text ?? string.Empty;
        Banner.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowBannerButton(string text, Action action)
    {
        BannerButton.Content = text;
        BannerButton.Visibility = Visibility.Visible;
        _bannerAction = action;
    }

    private void OnBannerButtonClick(object sender, RoutedEventArgs e) => _bannerAction?.Invoke();

    /// <summary>Переключить вкладку «Транскрипт» / «Заметка».</summary>
    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        // Первая вкладка отмечена в разметке, и Checked приходит ещё до
        // того, как InitializeComponent связал остальные элементы.
        if (TranscriptView is null || NoteBox is null || NoteTab is null)
        {
            return;
        }

        bool note = NoteTab.IsChecked == true;
        TranscriptView.Visibility = note ? Visibility.Collapsed : Visibility.Visible;
        NoteBox.Visibility = note ? Visibility.Visible : Visibility.Collapsed;

        if (!note)
        {
            SaveNote();
        }
    }

    // --- подсказки книги голосов ---------------------------------------------

    /// <summary>
    /// Кого книга голосов узнаёт среди неназванных голосов этого звонка.
    /// </summary>
    /// <remarks>
    /// Имя — это человек, а не голос: два неназванных голоса, похожих на
    /// Кирилла, — скорее всего, два куска Кирилла, и подсказка обоим верна.
    /// Раньше имя получал только более похожий, а уже данные на звонке имена
    /// не предлагались вовсе — и после «Разделить заново» знакомого человека,
    /// разваленного на два голоса, приходилось собирать по памяти.
    /// </remarks>
    private Dictionary<string, VoiceMatch> Suggest()
    {
        var result = new Dictionary<string, VoiceMatch>();
        if (!Settings.RememberVoices || _transcript is null || _session is null)
        {
            return result;
        }

        IEnumerable<string> unnamed = _transcript.Voices.Count == 0
            ? (_session.Participants.Count == 0 ? [CallVoices.WholeOtherSide] : [])
            : _transcript.Voices.Where(v => CallSpeakers.NameOf(_session, v) is null);

        foreach (string voice in unnamed)
        {
            if (_transcript.VoicePrints.TryGetValue(voice, out float[]? print)
                && _services.Voices.Match(print) is { } match)
            {
                result[voice] = match;
            }
        }

        return result;
    }

    /// <summary>Принять все подсказки разом — человек их видел в баннере.</summary>
    private void AcceptSuggestions()
    {
        foreach ((string voice, VoiceMatch match) in _suggestions.ToList())
        {
            if (voice == CallVoices.WholeOtherSide)
            {
                SetOtherSideName(match.Name);
            }
            else
            {
                SetNames([voice], match.Name);
            }
        }
    }

    /// <summary>
    /// «Похоже на: Кирилл · 84%» и кнопка «Верно» под ним.
    /// </summary>
    /// <remarks>
    /// Друг под другом, а не в строку: в строке с кнопкой имя и процент
    /// переносились на две строки и выглядели зажатыми.
    /// </remarks>
    private static StackPanel SuggestionRow(VoiceMatch match, Action accept)
    {
        var row = new StackPanel { Margin = new Thickness(0, Tokens.Space3, 0, 0) };

        TextBlock text = Ui.Body(string.Format(
            L.S.Formatting,
            L.S.VoiceSuggestion,
            match.Name,
            Math.Round(match.Score * 100).ToString(L.S.Formatting)));
        text.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        row.Children.Add(text);

        var confirm = new Wpf.Ui.Controls.Button
        {
            Content = L.S.VoiceAcceptSuggestion,
            Appearance = ControlAppearance.Primary,
            Margin = new Thickness(0, Tokens.Space2, 0, 0),
        };
        confirm.Click += (_, _) => accept();
        row.Children.Add(confirm);
        return row;
    }

    /// <summary>Запомнить слепок голоса под именем, если запоминание включено.</summary>
    private void LearnVoice(string voice, string? name)
    {
        if (name is not null
            && Settings.RememberVoices
            && _transcript?.VoicePrints.TryGetValue(voice, out float[]? print) == true)
        {
            _services.Voices.Learn(name, print);
        }
    }

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
        IReadOnlyDictionary<string, int> colors = CallPeople.Colors(People());
        string myName = Settings.EffectiveMyName;
        Brush primary = Resource("TextFillColorPrimaryBrush", Colors.Black);
        Brush secondary = Resource("TextFillColorSecondaryBrush", Colors.Gray);
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
                SpeakerBrush = BrushOf(line, colors),
                SpeakerForeground = named ? primary : secondary,
                SpeakerFontStyle = named ? FontStyles.Normal : FontStyles.Italic,
                SpeakerVisibility = speaker == previous ? Visibility.Collapsed : Visibility.Visible,
                PlayHint = string.Format(L.S.Formatting, L.S.LinePlayFrom, CallTranscriptRenderer.Stamp(line.Start)),
                IsCurrent = current == line.Start,
            });

            previous = speaker;
        }
    }

    /// <summary>Цвет реплики — цвет её человека: у голосов одного человека он общий.</summary>
    private static Brush BrushOf(CallLine line, IReadOnlyDictionary<string, int> colors)
    {
        if (line.Channel == CallChannel.Mine)
        {
            return VoicePalette.Me;
        }

        // Реплика без голоса — это собеседник, чьи голоса не разделялись:
        // цвет первого голоса, как у его карточки в панели.
        int color = line.Voice is { } voice && colors.TryGetValue(voice, out int known) ? known : 0;
        return color < 0 ? VoicePalette.Me : VoicePalette.For(color);
    }

    /// <summary>Участники открытого звонка — вы, названные и неназванные.</summary>
    private IReadOnlyList<CallPerson> People() =>
        _transcript is null || _session is null ? [] : CallPeople.Of(_session, _transcript);

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
                TitleBox.Text = TitleOf(_session);
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
    /// Пока человек своего не дал, в поле стоят имена участников. Сохранять
    /// их как «название» нельзя: тогда звонок не следовал бы за правкой
    /// участников и за сменой языка («Звонок» / «Call»).
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
        VoicesColumn.Width = visible ? new GridLength(320) : new GridLength(0);
    }

    /// <summary>
    /// Собрать панель участников: вы и собеседники — людьми, а не голосами.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Карточки собираются кодом, как карточки настроек: их число заранее
    /// неизвестно, а содержимое — чипы имён, цитаты с кнопками — проще
    /// сложить построителем, чем шаблоном с полудюжиной вложенных привязок.
    /// </para>
    /// <para>
    /// Названный человек свёрнут в строку с долей: внимание остаётся на
    /// карточках «Кто это?». Цитаты, его голоса и смена имени — по щелчку.
    /// </para>
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

        // Пока над звонком идёт работа — разделение, сверка, — предлагать
        // разделить ещё раз незачем: второе нажатие встало бы в очередь следом.
        bool busy = _services.LiveState(_session.Directory) is not null;
        IReadOnlyList<CallPerson> people = People();

        VoicesHost.Children.Add(PeopleHeader(people.Count, canResplit: !busy && _services.CanSplitVoices()));

        if (!busy && _resplitOpen && _services.CanSplitVoices())
        {
            VoicesHost.Children.Add(ResplitRow());
        }

        if (!busy && VoiceCheck.MoreVoicesLikely(_transcript, out TimeSpan doubtful))
        {
            VoicesHost.Children.Add(MoreVoicesCard(doubtful, Math.Max(_transcript.Voices.Count, 1)));
        }

        foreach (CallPerson person in people)
        {
            VoicesHost.Children.Add(PersonCard(person, people));
        }
    }

    /// <summary>«Участники · 3» и меню «⋯».</summary>
    /// <remarks>
    /// «Разделить заново» нужно редко, а постоянной ссылкой внизу панели
    /// читалось загадкой без контекста. Живёт в меню, как и у звонка.
    /// </remarks>
    private Grid PeopleHeader(int count, bool canResplit)
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, Tokens.Space3) };
        TextBlock title = Ui.BodyStrong(string.Format(L.S.Formatting, L.S.PeopleHeader, count));
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);

        if (canResplit)
        {
            var more = new Wpf.Ui.Controls.Button
            {
                Icon = new SymbolIcon { Symbol = SymbolRegular.MoreHorizontal24 },
                ToolTip = L.S.CallsMore,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            more.Click += (_, _) =>
            {
                var menu = new ContextMenu { PlacementTarget = more, Placement = PlacementMode.Bottom };
                var resplit = new MenuItem { Header = L.S.VoicesResplitMenu };
                resplit.Click += (_, _) =>
                {
                    _resplitOpen = true;
                    ShowVoices();
                };
                menu.Items.Add(resplit);
                menu.IsOpen = true;
            };
            header.Children.Add(more);
        }

        return header;
    }

    /// <summary>
    /// «Похоже, на звонке был ещё кто-то» — и разделить заново на голос больше.
    /// </summary>
    /// <remarks>
    /// Самая частая причина чужих цитат — не ошибка на отдельной реплике, а
    /// неверное число голосов: отметили одного, а говорили трое. Тогда чужие
    /// реплики не похожи ни на один найденный голос, и проверка это видит.
    /// Лечится не правкой по одной реплике, а разделением заново.
    /// </remarks>
    private Border MoreVoicesCard(TimeSpan doubtful, int found)
    {
        var body = new StackPanel();
        body.Children.Add(Ui.BodyStrong(L.S.VoicesMoreTitle));

        TextBlock hint = Ui.Caption(string.Format(L.S.Formatting, L.S.VoicesMoreHint, L.S.Duration(doubtful)));
        hint.Margin = new Thickness(0, Tokens.Space1, 0, 0);
        body.Children.Add(hint);

        if (_services.CanSplitVoices() && _session is not null)
        {
            string directory = _session.Directory;
            var split = new Wpf.Ui.Controls.Button
            {
                Content = string.Format(L.S.Formatting, L.S.VoicesSplitInto, found + 1),
                Appearance = ControlAppearance.Primary,
                Margin = new Thickness(0, Tokens.Space3, 0, 0),
            };
            split.Click += (_, _) => _ = _services.Resplit(directory, found + 1);
            body.Children.Add(split);
        }
        else if (ModelNeeds.Missing(Settings, ModelNeeds.SplitVoices) is { Count: > 0 } missing)
        {
            // Сказать, что кто-то ещё был, и не дать ничего сделать, — тупик:
            // так и было, пока модели разделения не хватало.
            TextBlock need = Ui.Caption(string.Format(L.S.Formatting, L.S.VoicesNeedModels, L.S.KindNames(missing)));
            need.Margin = new Thickness(0, Tokens.Space2, 0, 0);
            body.Children.Add(need);

            var get = new Wpf.Ui.Controls.Button
            {
                Content = L.S.VoicesOpenModels,
                Appearance = ControlAppearance.Primary,
                Margin = new Thickness(0, Tokens.Space2, 0, 0),
            };
            get.Click += (_, _) => _services.FetchModel(missing[0]);
            body.Children.Add(get);
        }

        Border card = Ui.Card(body);
        card.Margin = new Thickness(0, 0, 0, Tokens.Space2);
        card.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
        return card;
    }

    /// <summary>У кого из людей открыт выбор имени — по <see cref="KeyOf"/>.</summary>
    private readonly HashSet<string> _renaming = [];

    /// <summary>Какие карточки названных людей раскрыты — по <see cref="KeyOf"/>.</summary>
    private readonly HashSet<string> _expanded = [];

    /// <summary>Звонки, сверку которых уже попросили, — чтобы не просить на каждом обновлении.</summary>
    private readonly HashSet<string> _verifyAsked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Сколько цитат каждого голоса человек отклонил на этом звонке.
    /// </summary>
    /// <remarks>
    /// Одна чужая цитата — ошибка разделителя на одной реплике. Две из одного
    /// голоса — уже признак, что в голосе два человека.
    /// </remarks>
    private readonly Dictionary<string, int> _rejected = [];

    /// <summary>Раскрыт ли выбор числа голосов для переразделения.</summary>
    private bool _resplitOpen;

    /// <summary>Чем карточка человека помнит, раскрыта ли она, — между перерисовками.</summary>
    /// <remarks>Имя, а не голос: присоединили голос — человек тот же, и карточка тоже.</remarks>
    private static string KeyOf(CallPerson person) =>
        person.IsMe ? CallSession.Me : person.Name?.ToUpperInvariant() ?? person.Voices[0];

    private static bool IsWholeSide(CallPerson person) => person.Voices is [CallVoices.WholeOtherSide];

    private static Brush ColorOf(CallPerson person) =>
        person.IsMe ? VoicePalette.Me : VoicePalette.For(Math.Max(person.Color, 0));

    /// <summary>Карточка одного участника.</summary>
    /// <param name="person">Кто: вы, названный человек или неназванный голос.</param>
    /// <param name="people">Все на звонке — для чипов «уже здесь».</param>
    private Border PersonCard(CallPerson person, IReadOnlyList<CallPerson> people)
    {
        string key = KeyOf(person);
        bool whole = IsWholeSide(person);
        bool unnamed = !person.IsMe && person.Name is null;
        bool waiting = unnamed && !whole;
        bool renaming = _renaming.Contains(key);

        // Своё раскрывать есть что, только если к вам отнесли голоса с той
        // стороны; у собеседника — цитаты и имя.
        bool expandable = !unnamed && (!person.IsMe || person.Voices.Count > 0);
        bool open = unnamed || renaming || (expandable && _expanded.Contains(key));

        var body = new StackPanel();

        // Заголовок: цвет, имя, сколько говорил, доля от всего звонка.
        var title = new Grid();
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        title.Children.Add(Dot(ColorOf(person), 10));

        TextBlock nameText = person switch
        {
            { IsMe: true } => Ui.BodyStrong(Settings.EffectiveMyName),
            { Name: { } name } => Ui.BodyStrong(name),
            _ when whole => Ui.BodySecondary(OtherSideLabel(Settings)),
            _ => Ui.BodySecondary(L.S.VoiceWho),
        };
        if (waiting)
        {
            nameText.FontStyle = FontStyles.Italic;
        }

        nameText.TextWrapping = TextWrapping.NoWrap;
        nameText.TextTrimming = TextTrimming.CharacterEllipsis;
        nameText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(nameText, 1);
        title.Children.Add(nameText);

        TextBlock stats = Ui.Caption($"{L.S.Duration(person.Speech)} · {Math.Round(person.Share * 100).ToString(L.S.Formatting)}%");
        stats.Margin = new Thickness(Tokens.Space2, 0, 0, 0);
        stats.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(stats, 2);
        title.Children.Add(stats);

        if (expandable && !renaming)
        {
            var chevron = new SymbolIcon
            {
                Symbol = open ? SymbolRegular.ChevronUp16 : SymbolRegular.ChevronDown16,
                FontSize = 12,
                Margin = new Thickness(Tokens.Space2, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            chevron.SetResourceReference(ForegroundProperty, "TextFillColorTertiaryBrush");
            Grid.SetColumn(chevron, 3);
            title.Children.Add(chevron);

        }

        // Заголовок — кнопка, а не щелчок по сетке: раскрыть карточку должно
        // быть можно и с клавиатуры, и экранным диктором.
        if (expandable && !renaming)
        {
            Button toggle = Ui.Link(string.Empty);
            toggle.Content = title;
            toggle.Padding = new Thickness(Tokens.Space1);
            toggle.Margin = new Thickness(-Tokens.Space1);
            toggle.HorizontalAlignment = HorizontalAlignment.Stretch;
            toggle.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
            System.Windows.Automation.AutomationProperties.SetName(toggle, nameText.Text);
            toggle.Click += (_, _) =>
            {
                if (!_expanded.Remove(key))
                {
                    _expanded.Add(key);
                }

                ShowVoices();
            };
            body.Children.Add(toggle);
        }
        else
        {
            body.Children.Add(title);
        }
        body.Children.Add(ShareBar(person.Share, ColorOf(person)));

        if (person.IsMe)
        {
            TextBlock mine = Ui.Caption(L.S.VoiceMe);
            mine.Margin = new Thickness(0, Tokens.Space1, 0, 0);
            body.Children.Add(mine);
        }

        if (open)
        {
            AddPersonDetails(body, person, people, key, unnamed, renaming);
        }

        Border card = Ui.Card(body);
        card.Margin = new Thickness(0, 0, 0, Tokens.Space2);

        // Голос ждёт имени — рамка акцентом: это то, ради чего сюда пришли.
        if (waiting)
        {
            card.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
        }

        return card;
    }

    /// <summary>Раскрытая часть карточки: подсказка, цитаты, голоса, выбор имени.</summary>
    private void AddPersonDetails(
        StackPanel body,
        CallPerson person,
        IReadOnlyList<CallPerson> people,
        string key,
        bool unnamed,
        bool renaming)
    {
        string first = person.Voices.Count > 0 ? person.Voices[0] : string.Empty;

        if (unnamed && _rejected.GetValueOrDefault(first) >= 2 && !IsWholeSide(person))
        {
            body.Children.Add(RejectedHint());
        }

        if (unnamed && _suggestions.TryGetValue(first, out VoiceMatch? suggested))
        {
            body.Children.Add(SuggestionRow(suggested, () => NamePerson(person, suggested.Name, people)));
        }

        if (person.Quotes.Count > 0)
        {
            var quotes = new StackPanel { Margin = new Thickness(0, Tokens.Space3, 0, 0) };
            foreach (CallLine quote in person.Quotes)
            {
                quotes.Children.Add(QuoteRow(quote));
            }

            body.Children.Add(quotes);
        }

        // Из каких голосов человек собран — и «отделить» у каждого: промах
        // чипом исправляется одним щелчком, а не повторным разделением.
        if (person.Voices.Count > (person.IsMe ? 0 : 1))
        {
            var pieces = new StackPanel { Margin = new Thickness(0, Tokens.Space2, 0, 0) };
            foreach (string voice in person.Voices)
            {
                pieces.Children.Add(VoicePiece(voice));
            }

            body.Children.Add(pieces);
        }

        if (person.IsMe)
        {
            return;
        }

        if (unnamed || renaming)
        {
            WrapPanel chips = NameChips(person, people, picked =>
            {
                _renaming.Remove(key);
                NamePerson(person, picked, people);
            });
            chips.Margin = new Thickness(0, Tokens.Space3, 0, 0);
            body.Children.Add(chips);
            return;
        }

        // Имя уже есть — выбор свёрнут в «Изменить»: шесть чипов под
        // названным человеком — это шум, а не помощь. «Изменить» — на этом
        // звонке, «Переименовать везде» — правда о человеке во всех.
        Button change = Ui.Link(L.S.VoiceChange);
        change.Click += (_, _) =>
        {
            _renaming.Add(key);
            ShowVoices();
        };

        Button everywhere = Ui.Link(L.S.PersonRenameEverywhere);
        everywhere.Margin = new Thickness(Tokens.Space4, 0, 0, 0);
        everywhere.Click += (_, _) =>
        {
            if (person.Name is { } name && PersonRenamer.Run(
                    Window.GetWindow(this), _services.Settings, _services.Voices, _services.CallsDirectory(), name, to: null, _services.Render))
            {
                RefreshAfterEdit();
            }
        };

        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, Tokens.Space1, 0, 0),
            Children = { change, everywhere },
        });
    }

    /// <summary>«Голос B · 12 с   Отделить».</summary>
    private StackPanel VoicePiece(string voice)
    {
        TimeSpan speech = TimeSpan.FromSeconds(_transcript!.Lines
            .Where(l => l.Voice == voice)
            .Sum(l => Math.Max(0, (l.End - l.Start).TotalSeconds)));

        TextBlock label = Ui.Caption($"{L.S.TranscriptVoice} {voice} · {L.S.Duration(speech)}");
        label.VerticalAlignment = VerticalAlignment.Center;

        Button detach = Ui.Link(L.S.VoiceDetach);
        detach.FontSize = Tokens.Caption;
        detach.Padding = new Thickness(4, 0, 4, 0);
        detach.Margin = new Thickness(Tokens.Space2, 0, 0, 0);
        detach.VerticalAlignment = VerticalAlignment.Center;
        detach.Click += (_, _) => SetNames([voice], null);

        return new StackPanel { Orientation = Orientation.Horizontal, Children = { label, detach } };
    }

    private static System.Windows.Shapes.Ellipse Dot(Brush color, double size) => new()
    {
        Width = size,
        Height = size,
        Fill = color,
        Margin = new Thickness(0, 0, Tokens.Space2, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Grid ShareBar(double share, Brush color)
    {
        var bar = new Grid { Height = 4, Margin = new Thickness(0, Tokens.Space2, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(share, 0.001), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - share, 0.001), GridUnitType.Star) });

        var track = new Border { CornerRadius = new CornerRadius(2) };
        track.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorTertiaryBrush");
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = color });
        return bar;
    }

    /// <summary>Цитата с кнопкой прослушивания.</summary>
    /// <summary>«Две цитаты отсюда оказались чужими» — и разделить на голос больше.</summary>
    private StackPanel RejectedHint()
    {
        var panel = new StackPanel { Margin = new Thickness(0, Tokens.Space3, 0, 0) };
        panel.Children.Add(Ui.Caption(L.S.VoiceRejectedHint));

        if (_services.CanSplitVoices() && _session is not null && _transcript is not null)
        {
            string directory = _session.Directory;
            int wanted = _transcript.Voices.Count + 1;
            Button split = Ui.Link(string.Format(L.S.Formatting, L.S.VoicesSplitInto, wanted));
            split.HorizontalAlignment = HorizontalAlignment.Left;
            split.Margin = new Thickness(0, Tokens.Space1, 0, 0);
            split.Click += (_, _) => _ = _services.Resplit(directory, wanted);
            panel.Children.Add(split);
        }

        return panel;
    }

    /// <summary>
    /// «Не этот человек» под цитатой: куда её отдать.
    /// </summary>
    /// <remarks>
    /// Раньше чужая цитата в карточке была тупиком: её было видно, а
    /// исправить можно было только в транскрипте, найдя там ту же реплику.
    /// Если голоса не разделялись, переселять цитату некуда — остаётся
    /// разделить голоса.
    /// </remarks>
    private void ShowNotThisMenu(FrameworkElement anchor, CallLine quote)
    {
        if (_transcript is null || _session is null)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        if (_transcript.Voices.Count == 0)
        {
            menu.Items.Add(SplitOrGetModelItem(_session.Directory));
        }
        else
        {
            AddSaidBy(menu, quote, reject: true);
        }

        menu.IsOpen = true;
    }

    /// <summary>
    /// Кому отдать реплику: люди на звонке, вы, кто-то другой.
    /// </summary>
    /// <param name="menu">Куда добавить пункты.</param>
    /// <param name="line">Реплика собеседника.</param>
    /// <param name="reject">
    /// Из «Не этот человек» под цитатой: текущего человека в списке нет, и
    /// отказ считается — два отказа из одного голоса подсказывают, что в нём
    /// двое.
    /// </param>
    private void AddSaidBy(ItemsControl menu, CallLine line, bool reject)
    {
        foreach (CallPerson person in People())
        {
            bool current = line.Voice is { } voice && person.Voices.Contains(voice);
            if (reject && current)
            {
                continue;
            }

            string header = person switch
            {
                { IsMe: true } => Settings.EffectiveMyName,
                { Name: { } name } => name,
                _ => $"{L.S.TranscriptVoice} {person.Voices[0]}",
            };

            var item = new MenuItem
            {
                Header = header,
                IsCheckable = !reject,
                IsChecked = current,
                Icon = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = ColorOf(person) },
            };

            CallPerson target = person;
            item.Click += (_, _) =>
            {
                if (current)
                {
                    return;
                }

                if (reject && line.Voice is { } from)
                {
                    _rejected[from] = _rejected.GetValueOrDefault(from) + 1;
                }

                // Своих голосов на той стороне может ещё не быть — тогда
                // реплика становится новым голосом, сразу названным «я».
                if (target.Voices.Count > 0)
                {
                    ReassignLine(line, target.Voices[0]);
                }
                else
                {
                    ReassignLine(line, FreshVoice(), CallSession.Me);
                }
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        // Новый голос — следующая свободная буква: реплика станет отдельной
        // карточкой «Кто это?».
        var someone = new MenuItem { Header = L.S.VoiceSomeoneElse };
        someone.Click += (_, _) =>
        {
            if (reject && line.Voice is { } from)
            {
                _rejected[from] = _rejected.GetValueOrDefault(from) + 1;
            }

            ReassignLine(line, FreshVoice());
        };
        menu.Items.Add(someone);
    }

    /// <summary>Следующая свободная буква голоса.</summary>
    private string FreshVoice()
    {
        IReadOnlyList<string> voices = _transcript!.Voices;
        string fresh = CallVoices.Letter(voices.Count);
        for (int i = voices.Count; voices.Contains(fresh); i++)
        {
            fresh = CallVoices.Letter(i + 1);
        }

        return fresh;
    }

    /// <summary>
    /// Пункт «Кто-то другой» там, где голоса не разделялись: разделить —
    /// или, если нечем, скачать чем.
    /// </summary>
    /// <remarks>
    /// Раньше без модели разделения пункт был просто серым. Человек видел,
    /// что кто-то ещё был на звонке, и не мог узнать, почему с этим ничего
    /// нельзя сделать.
    /// </remarks>
    private MenuItem SplitOrGetModelItem(string directory)
    {
        if (_services.CanSplitVoices())
        {
            var split = new MenuItem { Header = L.S.VoiceSomeoneElseSplit };
            split.Click += (_, _) => _ = _services.Resplit(directory, 2);
            return split;
        }

        var get = new MenuItem { Header = L.S.VoiceSomeoneElseGetModel };
        IReadOnlyList<Tapybara.Core.Models.ModelKind> missing = ModelNeeds.Missing(Settings, ModelNeeds.SplitVoices);
        get.IsEnabled = missing.Count > 0;
        get.Click += (_, _) => _services.FetchModel(missing[0]);
        return get;
    }

    private Grid QuoteRow(CallLine quote)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, Tokens.Space2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
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
            CornerRadius = Tokens.PillRadius,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = L.S.VoicePlayQuote,
        };
        play.Click += (_, _) => PlayQuote(quote);
        row.Children.Add(play);

        var text = new StackPanel();
        text.Children.Add(Ui.Body($"«{quote.Text}»"));
        TextBlock stamp = Ui.Caption(CallTranscriptRenderer.Stamp(quote.Start));
        stamp.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        stamp.VerticalAlignment = VerticalAlignment.Center;
        stamp.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");

        Button notThis = Ui.Link(L.S.VoiceNotThis);
        notThis.FontSize = Tokens.Caption;
        notThis.Padding = new Thickness(4, 0, 4, 0);
        notThis.Margin = new Thickness(Tokens.Space2, 0, 0, 0);
        notThis.VerticalAlignment = VerticalAlignment.Center;
        notThis.Click += (_, _) => ShowNotThisMenu(notThis, quote);

        text.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { stamp, notThis },
        });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    /// <summary>
    /// Чипы имён: кто подходит на этот голос.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Первыми — подсказанные книгой голосов и те, кто уже есть на звонке, с
    /// цветной точкой: выбрать такого — значит присоединить голос к нему.
    /// «Это я» — для своего голоса, попавшего в чужую дорожку через динамики.
    /// </para>
    /// <para>
    /// Дальше отмеченные на карточке после звонка и знакомые — свежие первыми.
    /// Искать нужное имя среди шестидесяти незачем: а если его нет,
    /// «другое…» открывает поле.
    /// </para>
    /// </remarks>
    private WrapPanel NameChips(CallPerson person, IReadOnlyList<CallPerson> people, Action<string?> pick)
    {
        var chips = new WrapPanel();
        Style style = Ui.ChipStyle();
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string label, Brush? dot)
        {
            if (!shown.Add(name))
            {
                return;
            }

            object content = label;
            if (dot is not null)
            {
                System.Windows.Shapes.Ellipse mark = Dot(dot, 8);
                mark.Margin = new Thickness(0, 0, Tokens.Space2, 0);

                // Цвет текста — от кнопки, а не от общего стиля TextBlock:
                // тот чёрный, и у отмеченного чипа на синем имя не читалось.
                var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
                text.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding
                {
                    Path = new PropertyPath(System.Windows.Documents.TextElement.ForegroundProperty),
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ContentPresenter), 1),
                });

                content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { mark, text },
                };
            }

            var chip = new ToggleButton
            {
                Content = content,
                IsChecked = string.Equals(name, person.Name, StringComparison.OrdinalIgnoreCase),
                Style = style,
            };
            System.Windows.Automation.AutomationProperties.SetName(chip, label);
            // Checked/Unchecked, а не Click: щелчок — лишь один из способов
            // переключить чип. Экранный диктор и UI Automation переключают
            // его через TogglePattern, и Click при этом не приходит вовсе.
            chip.Checked += (_, _) => pick(name);
            chip.Unchecked += (_, _) => pick(null);
            chips.Children.Add(chip);
        }

        if (person.Name is { } own)
        {
            Add(own, own, ColorOf(person));
        }

        foreach (string hinted in person.Voices.Where(_suggestions.ContainsKey).Select(v => _suggestions[v].Name))
        {
            Add(hinted, hinted, people.FirstOrDefault(p => string.Equals(p.Name, hinted, StringComparison.OrdinalIgnoreCase)) is { } there ? ColorOf(there) : null);
        }

        foreach (CallPerson there in people.Where(p => p.Name is not null))
        {
            Add(there.Name!, there.Name!, ColorOf(there));
        }

        if (!IsWholeSide(person))
        {
            Add(CallSession.Me, L.S.VoiceItsMe, VoicePalette.Me);
        }

        int limit = shown.Count + _session!.Participants.Count + SuggestedNames;
        foreach (string known in _session.Participants.Concat(Settings.KnownParticipants))
        {
            if (shown.Count >= limit)
            {
                break;
            }

            Add(known, known, null);
        }

        var other = new ToggleButton
        {
            Content = L.S.VoiceOtherName,
            Style = style,
        };
        other.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");

        other.Checked += (_, _) =>
        {
            int at = chips.Children.IndexOf(other);
            chips.Children.Remove(other);

            var entry = new Wpf.Ui.Controls.TextBox
            {
                PlaceholderText = L.S.VoiceNamePlaceholder,
                Width = 150,
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
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, Tokens.Space3) };

        // Выбор числа и отдельная кнопка — а не ряд цифр, где нажатие на
        // цифру сразу запускало разделение: передумать было нельзя, а
        // промахнуться — легко. «Отмена» сворачивает всё обратно в ссылку.
        TextBlock hint = Ui.Caption(L.S.VoicesResplitCount);
        hint.Margin = new Thickness(0, 0, 0, Tokens.Space1);
        panel.Children.Add(hint);

        int found = Math.Max(_transcript!.Voices.Count, 1);
        var count = new System.Windows.Controls.ComboBox { MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Left };
        for (int n = 2; n <= 8; n++)
        {
            count.Items.Add(n);
        }

        count.SelectedItem = Math.Clamp(found + 1, 2, 8);
        panel.Children.Add(count);

        var go = new Wpf.Ui.Controls.Button
        {
            Content = L.S.VoicesResplitGo,
            Appearance = ControlAppearance.Primary,
            Margin = new Thickness(0, 0, Tokens.Space2, 0),
        };
        go.Click += (_, _) =>
        {
            if (count.SelectedItem is int wanted && _session is not null)
            {
                _resplitOpen = false;
                _ = _services.Resplit(_session.Directory, wanted);
                Reload(keepSelection: true);
            }
        };

        var cancel = new Wpf.Ui.Controls.Button { Content = L.S.ButtonCancel };
        cancel.Click += (_, _) =>
        {
            _resplitOpen = false;
            ShowVoices();
        };

        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, Tokens.Space3, 0, 0),
            Children = { go, cancel },
        });

        return panel;
    }

    /// <summary>
    /// Назвать человека — все его голоса разом.
    /// </summary>
    /// <remarks>
    /// Имя — это человек. Имя того, кто уже есть на звонке, присоединяет
    /// голос к нему; новое имя у названного — переименование на этом звонке.
    /// Раньше второй голос с тем же именем отнимал имя у первого, а склеить
    /// два куска одного человека можно было только безвозвратно.
    /// </remarks>
    private void NamePerson(CallPerson person, string? name, IReadOnlyList<CallPerson> people)
    {
        if (person.IsMe)
        {
            return;
        }

        // «павел» и «Павел» — один человек; пишем так, как он уже назван.
        if (name is not null && people.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is { Name: { } same })
        {
            name = same;
        }

        if (IsWholeSide(person))
        {
            SetOtherSideName(name);
            return;
        }

        SetNames(person.Voices, name);
    }

    /// <summary>Дать голосам имя — или снять его, отделив голоса от человека.</summary>
    private void SetNames(IReadOnlyList<string> voices, string? name)
    {
        if (_session is null)
        {
            return;
        }

        string directory = _session.Directory;
        _session = CallMeta.Update(directory, s =>
        {
            var names = new Dictionary<string, string>(s.VoiceNames);
            foreach (string voice in voices)
            {
                if (name is null)
                {
                    names.Remove(voice);
                }
                else
                {
                    names[voice] = name;
                }
            }

            return s with { VoiceNames = names };
        }) ?? _session;

        if (name != CallSession.Me)
        {
            RememberName(name);
            foreach (string voice in voices)
            {
                LearnVoice(voice, name);
            }
        }

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
        LearnVoice(CallVoices.WholeOtherSide, name);
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

    /// <summary>Отдать реплику другому голосу.</summary>
    /// <param name="line">Реплика.</param>
    /// <param name="voice">Голос.</param>
    /// <param name="name">Имя нового голоса — когда голос заводится ради этой реплики.</param>
    private void ReassignLine(CallLine line, string voice, string? name = null)
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

        _session = CallMeta.Update(directory, s => s with
        {
            Voices = changed.Voices,
            VoiceNames = name is null ? s.VoiceNames : new Dictionary<string, string>(s.VoiceNames) { [voice] = name },
        }) ?? _session;
        _services.Render(directory);
        RefreshAfterEdit();

        // Слепок голоса снят и с этой реплики — снять заново без неё, иначе
        // чужой голос остался бы в подсказках и лёг бы в книгу голосов.
        _ = _services.RefreshPrints(directory);
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

        if (row.Line.Channel == CallChannel.Theirs && _transcript is { Voices.Count: > 0 })
        {
            menu.Items.Add(new MenuItem { Header = L.S.LineSaidBy, IsEnabled = false });
            AddSaidBy(menu, row.Line, reject: false);
            menu.Items.Add(new Separator());
        }
        else if (row.Line.Channel == CallChannel.Theirs && _transcript is { Voices.Count: 0 } && _session is not null)
        {
            // Голоса не разделялись — отдать реплику некому, кроме как
            // разделив голоса: «кто-то другой» здесь и значит «их было больше».
            menu.Items.Add(SplitOrGetModelItem(_session.Directory));
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

        if (box is not null)
        {
            // Выделено несколько слов — правится выделенное: распознавание
            // режет незнакомое слово надвое, «рек стат», и двойной щелчок
            // берёт только половину.
            string selected = box.SelectedText.Trim();
            var fix = new MenuItem
            {
                Header = selected.Length == 0
                    ? L.S.LineFixWord
                    : string.Format(L.S.Formatting, L.S.LineFixSelection, selected.Length > 30 ? selected[..30] + "…" : selected),
                Icon = new SymbolIcon { Symbol = SymbolRegular.TextEditStyle24 },
            };
            int start = box.SelectionLength > 0 ? box.SelectionStart : box.CaretIndex;
            int length = box.SelectionLength;
            fix.Click += (_, _) => FixWordAt(box, row, start, length);
            menu.Items.Add(fix);

            var edit = new MenuItem { Header = L.S.LineEditText, Icon = new SymbolIcon { Symbol = SymbolRegular.Edit24 } };
            edit.Click += (_, _) => EditLineInPlace(box, row);
            menu.Items.Add(edit);
        }

        return menu;
    }

    // --- правка текста --------------------------------------------------------

    /// <summary>Двойной щелчок по слову реплики — исправить его.</summary>
    private void OnLineDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox { IsReadOnly: true, Tag: TranscriptLineRow row } box)
        {
            e.Handled = true;
            int index = box.GetCharacterIndexFromPoint(e.GetPosition(box), snapToText: true);

            // Карточка открывается, когда кнопку отпустят: на втором нажатии
            // поле ещё держит мышь для выделения, и отпускание закрыло бы
            // только что открытую карточку.
            void Open(object s, MouseButtonEventArgs up)
            {
                box.PreviewMouseLeftButtonUp -= Open;
                Dispatcher.BeginInvoke(() => FixWordAt(box, row, index), DispatcherPriority.Input);
            }

            box.PreviewMouseLeftButtonUp += Open;
        }
    }

    /// <summary>
    /// Исправить слово: здесь, во всём звонке вместе с похожими написаниями и, по желанию, в словаре.
    /// </summary>
    /// <remarks>
    /// Раньше отсюда можно было только добавить замену в словарь, и к этому
    /// звонку она не применялась — окно советовало распознать его заново.
    /// Теперь правится сам текст: миллисекунды, без Whisper.
    /// </remarks>
    private void FixWordAt(TextBox box, TranscriptLineRow row, int index, int length = 0)
    {
        if (_transcript is null || _session is null || TranscriptEdit.PhraseAt(row.Line.Text, index, length) is not { } word)
        {
            return;
        }

        string original = row.Line.Text.Substring(word.Start, word.Length);
        string directory = _session.Directory;
        box.Select(word.Start, word.Length);

        var field = new Wpf.Ui.Controls.TextBox { Text = original, MinWidth = 220, ClearButtonEnabled = false };

        var was = new TextBlock
        {
            Text = original,
            TextDecorations = TextDecorations.Strikethrough,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Tokens.Space2, 0),
        };
        was.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

        TextBlock arrow = Ui.Caption("→");
        arrow.VerticalAlignment = VerticalAlignment.Center;
        arrow.Margin = new Thickness(0, 0, Tokens.Space2, 0);

        var body = new StackPanel { Width = 360 };
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { was, arrow, field } });

        // Похожие написания — отмеченными: чаще всего их и нужно поменять
        // вместе. Лишнее снимается одним щелчком.
        IReadOnlyList<TranscriptEdit.WordForm> forms = TranscriptEdit.SimilarForms(_transcript, original);
        var checks = new List<(System.Windows.Controls.CheckBox Box, TranscriptEdit.WordForm Form)>();
        if (forms.Count > 1)
        {
            TextBlock similar = Ui.Caption(L.S.FixSimilar);
            similar.Margin = new Thickness(0, Tokens.Space3, 0, Tokens.Space1);
            body.Children.Add(similar);

            var list = new WrapPanel();
            foreach (TranscriptEdit.WordForm form in forms)
            {
                var check = new System.Windows.Controls.CheckBox
                {
                    Content = $"{form.Form} ×{form.Count}",
                    IsChecked = true,
                    Margin = new Thickness(0, 0, Tokens.Space3, 0),
                };
                checks.Add((check, form));
                list.Children.Add(check);
            }

            body.Children.Add(list);
        }

        var remember = new System.Windows.Controls.CheckBox
        {
            Content = L.S.FixRemember,
            IsChecked = true,
            Margin = new Thickness(0, Tokens.Space2, 0, 0),
        };
        body.Children.Add(remember);

        var replaceAll = new Wpf.Ui.Controls.Button { Appearance = ControlAppearance.Primary, Margin = new Thickness(0, 0, Tokens.Space2, 0) };
        var onlyHere = new Wpf.Ui.Controls.Button { Content = L.S.FixOnlyHere };
        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, Tokens.Space3, 0, 0),
            Children = { replaceAll, onlyHere },
        });

        List<string> Chosen() => checks.Count == 0
            ? [original]
            : [.. checks.Where(c => c.Box.IsChecked == true).Select(c => c.Form.Form)];

        void Update()
        {
            int count = checks.Count == 0 ? 1 : checks.Where(c => c.Box.IsChecked == true).Sum(c => c.Form.Count);
            string to = field.Text.Trim();
            replaceAll.Content = string.Format(L.S.Formatting, L.S.FixReplaceAll, count);
            replaceAll.IsEnabled = count > 0 && to.Length > 0 && to != original;
            onlyHere.IsEnabled = to.Length > 0 && to != original;
        }

        foreach ((System.Windows.Controls.CheckBox check, _) in checks)
        {
            check.Checked += (_, _) => Update();
            check.Unchecked += (_, _) => Update();
        }

        field.TextChanged += (_, _) => Update();
        Update();

        var card = new Border
        {
            Child = body,
            Padding = new Thickness(Tokens.Space4),
            CornerRadius = Tokens.CardRadius,
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.18 },
            Margin = new Thickness(Tokens.Space2),
        };
        card.SetResourceReference(Border.BackgroundProperty, "ApplicationBackgroundBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");

        Rect at = box.GetRectFromCharacterIndex(word.Start);
        var popup = new Popup
        {
            Child = card,
            PlacementTarget = box,
            Placement = PlacementMode.Bottom,
            PlacementRectangle = at,
            StaysOpen = false,
            AllowsTransparency = true,
        };

        void Apply(Func<CallTranscript, CallTranscript> change, IReadOnlyList<string>? rememberForms)
        {
            popup.IsOpen = false;
            string to = field.Text.Trim();
            if (CallTranscriptStore.Update(directory, change) is null)
            {
                return;
            }

            if (rememberForms is { Count: > 0 })
            {
                _services.Settings.Update(s =>
                {
                    var map = new Dictionary<string, string>(s.Replacements);
                    foreach (string form in rememberForms.Where(f => !f.Equals(to, StringComparison.OrdinalIgnoreCase)))
                    {
                        map[form] = to;
                    }

                    return s with { Replacements = map };
                });
            }

            _services.Render(directory);
            RefreshAfterEdit();
        }

        replaceAll.Click += (_, _) =>
        {
            List<string> chosen = Chosen();
            string to = field.Text.Trim();
            Apply(t => TranscriptEdit.Replace(t, chosen, to).Transcript, remember.IsChecked == true ? chosen : null);
        };

        // «Только здесь» — правка одного места, в словарь не идёт: это
        // исправление, которое разносить не нужно.
        onlyHere.Click += (_, _) =>
        {
            string to = field.Text.Trim();
            Apply(t => TranscriptEdit.ReplaceAt(t, row.Line, word.Start, word.Length, to), null);
        };

        // PreviewKeyDown: Enter поле WPF-UI обрабатывает само, и до KeyDown он не доходил.
        field.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && replaceAll.IsEnabled)
            {
                e.Handled = true;
                replaceAll.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                popup.IsOpen = false;
            }
        };

        // Всплывающее окно — отдельное окно Windows, и фокус клавиатуры сам к
        // нему не переходит: поле выглядело выделенным, а буквы уходили в
        // реплику под ним, которая только для чтения. Отдаём фокус окну
        // карточки явно, и лишь потом — полю.
        popup.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(card) is System.Windows.Interop.HwndSource source)
            {
                NativeFocus.Set(source.Handle);
            }

            field.Focus();
            Keyboard.Focus(field);
            field.SelectAll();
        };
        popup.Closed += (_, _) => PageRoot.Children.Remove(popup);
        PageRoot.Children.Add(popup);
        popup.IsOpen = true;
    }

    /// <summary>
    /// Исправить реплику целиком — прямо на месте: Enter сохраняет, Esc отменяет.
    /// </summary>
    private void EditLineInPlace(TextBox box, TranscriptLineRow row)
    {
        if (_session is null)
        {
            return;
        }

        string directory = _session.Directory;
        string before = row.Line.Text;
        bool done = false;

        void Finish(bool save)
        {
            if (done)
            {
                return;
            }

            done = true;
            box.IsReadOnly = true;
            string text = box.Text.Trim();
            if (!save || text.Length == 0 || text == before)
            {
                box.Text = before;
                return;
            }

            if (CallTranscriptStore.Update(directory, t => TranscriptEdit.EditLine(t, row.Line, text)) is not null)
            {
                _services.Render(directory);
                RefreshAfterEdit();
            }
        }

        box.IsReadOnly = false;
        box.Focus();
        box.CaretIndex = box.Text.Length;

        box.PreviewKeyDown += (_, e) =>
        {
            if (done)
            {
                return;
            }

            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                Finish(save: true);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Finish(save: false);
            }
        };
        box.LostKeyboardFocus += (_, _) => Finish(save: true);
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
        if (sender is not FrameworkElement { Tag: TranscriptLineRow row })
        {
            return;
        }

        if (row.IsPlaying)
        {
            _player.Stop();
            return;
        }

        PlayFrom(row.Line.Start);
    }

    /// <summary>
    /// Щелчок по реплике — отсюда «Слушать» и начнёт.
    /// </summary>
    /// <remarks>
    /// Полоса звучащей реплики — это и место, откуда продолжится
    /// прослушивание. Раньше поставить её можно было только проиграв запись
    /// до нужного места. Пока звук идёт, щелчок ничего не переносит: человек
    /// ставит курсор, чтобы выделить или поправить слово, а не перескочить.
    /// </remarks>
    private void OnLinePressed(object sender, MouseButtonEventArgs e)
    {
        if (_player.IsPlaying || sender is not FrameworkElement { DataContext: TranscriptLineRow row })
        {
            return;
        }

        foreach (TranscriptLineRow line in _lines)
        {
            line.IsCurrent = line == row;
            line.Progress = 0;
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

    /// <summary>
    /// Подсветить реплику, которую сейчас слышно, и вести за ней список.
    /// </summary>
    /// <remarks>
    /// Список идёт за звуком, только пока человек сам на него смотрит: если
    /// прежняя звучащая реплика видна. Прокрутил вверх перечитать — звук
    /// играет дальше, а список не выдёргивается из-под глаз.
    /// </remarks>
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

        TranscriptLineRow? before = _lines.FirstOrDefault(l => l.IsCurrent);
        bool sweep = Settings.ShowPlayingProgress;
        foreach (TranscriptLineRow row in _lines)
        {
            row.IsCurrent = row == now;
            row.IsPlaying = row == now && _playingQuote is null;
            row.Progress = row == now && sweep ? Fraction(row.Line, position) : 0;
        }

        if (now is not null && now != before && (before is null || IsOnScreen(before)))
        {
            LineList.ScrollIntoView(now);
        }
    }

    private static double Fraction(CallLine line, TimeSpan position)
    {
        double length = (line.End - line.Start).TotalSeconds;
        return length <= 0 ? 1 : Math.Clamp((position - line.Start).TotalSeconds / length, 0, 1);
    }

    /// <summary>Видна ли строка в списке целиком или частью.</summary>
    private bool IsOnScreen(TranscriptLineRow row)
    {
        if (LineList.ItemContainerGenerator.ContainerFromItem(row) is not FrameworkElement item || !item.IsVisible)
        {
            return false;
        }

        double top = item.TransformToAncestor(LineList).Transform(new System.Windows.Point(0, 0)).Y;
        return top + item.ActualHeight > 0 && top < LineList.ActualHeight;
    }

    private void OnPlayerStopped()
    {
        _playbackTimer.Stop();

        // Остановились — заливка уходит, полоса остаётся: с этого места
        // «Слушать» продолжит.
        foreach (TranscriptLineRow row in _lines)
        {
            row.Progress = 0;
            row.IsPlaying = false;
        }

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

    /// <summary>
    /// Скопировать транскрипт, подписав людей псевдонимами.
    /// </summary>
    /// <remarks>
    /// Показать разговор кому-то ещё и не показать, кто в нём был. Файлы
    /// звонка не меняются — меняется только копия (<see cref="PersonNames.Anonymize"/>).
    /// Псевдонимы запоминаются: у Кирилла, однажды ставшего «Шерифом»,
    /// следующая копия предложит того же «Шерифа».
    /// </remarks>
    private void CopyAnonymized()
    {
        if (_session is null || _transcript is null)
        {
            return;
        }

        string me = Settings.EffectiveMyName;
        IReadOnlyList<CallPerson> people = People();
        List<(string Name, Brush? Color)> named =
        [
            (me, VoicePalette.Me),
            .. people.Where(p => p is { IsMe: false, Name: not null }).Select(p => (p.Name!, (Brush?)ColorOf(p))),
        ];

        // И те, кого на звонке не было, но о ком говорили: «обсуждали с
        // Кириллом» выдаёт Кирилла не хуже подписи над репликой.
        foreach (string known in Settings.KnownParticipants)
        {
            if (!named.Any(n => string.Equals(n.Name, known, StringComparison.OrdinalIgnoreCase))
                && PersonNames.Find([_transcript], known).Count > 0)
            {
                named.Add((known, null));
            }
        }

        var window = new ConfirmWindow(
            L.S.AnonymizeTitle,
            L.S.AnonymizeBody,
            primaryButton: L.S.AnonymizeCopy,
            cancelButton: L.S.ButtonCancel,
            icon: SymbolRegular.PersonProhibited24);

        var rows = new Grid();
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fields = new List<(string Name, Wpf.Ui.Controls.TextBox Field)>();
        foreach ((string name, Brush? color) in named)
        {
            int row = rows.RowDefinitions.Count;
            rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Упомянутые без голоса — без точки: цвета у них на звонке нет.
            System.Windows.Shapes.Ellipse dot = Dot(color ?? System.Windows.Media.Brushes.Transparent, 8);
            var label = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { dot, Ui.Body(name) },
            };
            TextBlock arrow = Ui.Caption("→");
            arrow.VerticalAlignment = VerticalAlignment.Center;
            arrow.Margin = new Thickness(Tokens.Space3, 0, Tokens.Space3, 0);
            var field = new Wpf.Ui.Controls.TextBox
            {
                Text = Settings.PersonAliases.GetValueOrDefault(name, name),
                ClearButtonEnabled = false,
                Margin = new Thickness(0, 0, 0, Tokens.Space2),
            };

            Grid.SetRow(label, row);
            Grid.SetRow(arrow, row);
            Grid.SetColumn(arrow, 1);
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 2);
            rows.Children.Add(label);
            rows.Children.Add(arrow);
            rows.Children.Add(field);
            fields.Add((name, field));
        }

        window.AddContent(rows);

        Button numbered = Ui.Link(L.S.AnonymizeNumbered);
        numbered.HorizontalAlignment = HorizontalAlignment.Left;
        numbered.Click += (_, _) =>
        {
            // Себя не трогаем: чаще всего копию отдают от своего имени.
            for (int i = 1; i < fields.Count; i++)
            {
                fields[i].Field.Text = string.Format(L.S.Formatting, L.S.AnonymizeParticipant, i);
            }
        };
        window.AddContent(numbered);

        var mentions = new System.Windows.Controls.CheckBox { Content = L.S.AnonymizeMentions, IsChecked = true };
        window.AddContent(mentions);

        if (ConfirmWindow.Ask(Window.GetWindow(this), window) != ConfirmChoice.Primary)
        {
            return;
        }

        Dictionary<string, string> aliases = fields
            .Select(f => (f.Name, Alias: f.Field.Text.Trim()))
            .Where(f => f.Alias.Length > 0)
            .ToDictionary(f => f.Name, f => f.Alias);

        _services.Settings.Update(s =>
        {
            var remembered = new Dictionary<string, string>(s.PersonAliases);
            foreach ((string name, string alias) in aliases)
            {
                if (alias == name)
                {
                    remembered.Remove(name);
                }
                else
                {
                    remembered[name] = alias;
                }
            }

            return s with { PersonAliases = remembered };
        });

        (CallSession session, CallTranscript transcript) = PersonNames.Anonymize(_session, _transcript, aliases, mentions.IsChecked == true);
        Copy(CallTranscriptRenderer.Render(
            session,
            transcript,
            aliases.GetValueOrDefault(me, me),
            OtherSideLabel(Settings),
            L.S.TranscriptLabels));
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

        var anonymous = new MenuItem
        {
            Header = L.S.CallsCopyAnonymized,
            Icon = new SymbolIcon { Symbol = SymbolRegular.PersonProhibited24 },
            IsEnabled = _transcript is not null,
        };
        anonymous.Click += (_, _) => CopyAnonymized();
        menu.Items.Add(anonymous);

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

        // Ручные правки повторное распознавание сотрёт — спросить, а не
        // молча потерять полчаса исправлений.
        if (_transcript is { EditedByHand: true }
            && ConfirmWindow.Ask(Window.GetWindow(this), new ConfirmWindow(
                L.S.RetranscribeEditedTitle,
                L.S.RetranscribeEditedBody,
                primaryButton: L.S.CallsTranscribeAgain,
                cancelButton: L.S.ButtonCancel,
                icon: SymbolRegular.Warning24)) != ConfirmChoice.Primary)
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
