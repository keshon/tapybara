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
        string.Join(", ", session.Participants.Concat(session.VoiceNames.Values).Distinct(StringComparer.OrdinalIgnoreCase));

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
    /// Одно имя — одному голосу: если два голоса похожи на Кирилла, имя
    /// получает более похожий, а второму подсказки нет. Имена, уже данные
    /// голосам этого звонка, не предлагаются вовсе.
    /// </remarks>
    private Dictionary<string, VoiceMatch> Suggest()
    {
        var result = new Dictionary<string, VoiceMatch>();
        if (!Settings.RememberVoices || _transcript is null || _session is null)
        {
            return result;
        }

        List<string> taken = [.. _session.VoiceNames.Values];
        IEnumerable<string> unnamed = _transcript.Voices.Count == 0
            ? (_session.Participants.Count == 0 ? [CallVoices.WholeOtherSide] : [])
            : _transcript.Voices.Where(v => CallSpeakers.NameOf(_session, v) is null);

        List<(string Voice, VoiceMatch Match)> candidates = [];
        foreach (string voice in unnamed)
        {
            if (_transcript.VoicePrints.TryGetValue(voice, out float[]? print)
                && _services.Voices.Match(print, taken) is { } match)
            {
                candidates.Add((voice, match));
            }
        }

        foreach ((string voice, VoiceMatch match) in candidates.OrderByDescending(c => c.Match.Score))
        {
            if (!result.Values.Any(m => string.Equals(m.Name, match.Name, StringComparison.OrdinalIgnoreCase)))
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
                SetVoiceName(voice, match.Name);
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
                SpeakerBrush = BrushOf(line, voices),
                SpeakerForeground = named ? primary : secondary,
                SpeakerFontStyle = named ? FontStyles.Normal : FontStyles.Italic,
                SpeakerVisibility = speaker == previous ? Visibility.Collapsed : Visibility.Visible,
                PlayHint = string.Format(L.S.Formatting, L.S.LinePlayFrom, CallTranscriptRenderer.Stamp(line.Start)),
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

        // Реплика без голоса — это собеседник, чьи голоса не разделялись:
        // цвет первого голоса, как у его карточки в панели голосов.
        int index = line.Voice is null ? 0 : IndexOf(voices, line.Voice);
        return VoicePalette.For(Math.Max(index, 0));
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
    /// Собрать панель голосов: у каждого — доля речи, цитаты и выбор имени.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Карточки собираются кодом, как карточки настроек: их число заранее
    /// неизвестно, а содержимое — чипы имён, цитаты с кнопками — проще
    /// сложить построителем, чем шаблоном с полудюжиной вложенных привязок.
    /// </para>
    /// <para>
    /// Карточка одна на все случаи. Собеседник, голоса которого не
    /// разделялись, раньше получал другую, беднее — без доли речи и цитат,
    /// зато с «Кто это?» и шестью чипами, хотя имя было давно известно.
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

        IReadOnlyList<VoiceSummary> voices = CallVoices.Summarize(_transcript);

        var header = new Grid { Margin = new Thickness(0, 0, 0, Tokens.Space3) };
        header.Children.Add(Ui.BodyStrong(L.S.VoicesHeader));
        if (voices.Count > 1)
        {
            TextBlock found = Ui.Caption(string.Format(L.S.Formatting, L.S.VoicesFound, voices.Count));
            found.HorizontalAlignment = HorizontalAlignment.Right;
            found.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(found);
        }

        VoicesHost.Children.Add(header);

        // Пока над звонком идёт работа — разделение, сверка, — предлагать
        // разделить ещё раз незачем: второе нажатие встало бы в очередь следом.
        bool busy = _services.LiveState(_session.Directory) is not null;
        if (!busy && VoiceCheck.MoreVoicesLikely(_transcript, out TimeSpan doubtful))
        {
            VoicesHost.Children.Add(MoreVoicesCard(doubtful, Math.Max(voices.Count, 1)));
        }

        if (voices.Count == 0)
        {
            // Голоса не разделялись: весь чужой канал — один человек.
            string? name = _session.Participants.Count == 1 ? _session.Participants[0] : null;
            VoicesHost.Children.Add(VoiceCard(WholeSide(), 0, name, single: true, SetOtherSideName));
        }
        else
        {
            for (int i = 0; i < voices.Count; i++)
            {
                VoiceSummary voice = voices[i];
                VoicesHost.Children.Add(VoiceCard(
                    voice,
                    i,
                    CallSpeakers.NameOf(_session, voice.Id),
                    single: voices.Count == 1,
                    picked => SetVoiceName(voice.Id, picked)));
            }
        }

        VoicesHost.Children.Add(MeCard());

        // Переразделять есть что, только если голоса разделялись. На звонке
        // с одним отмеченным участником этот блок был загадкой без контекста.
        if (_services.CanSplitVoices() && _transcript.VoicesSplit)
        {
            VoicesHost.Children.Add(ResplitRow());
        }
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

    /// <summary>Сводка по чужому каналу целиком — когда голоса не разделялись.</summary>
    private VoiceSummary WholeSide()
    {
        var whole = new CallTranscript
        {
            Lines = [.. _transcript!.Lines.Where(l => l.Channel == CallChannel.Theirs).Select(l => l with { Voice = CallVoices.WholeOtherSide })],
        };

        IReadOnlyList<VoiceSummary> summary = CallVoices.Summarize(whole);
        return summary.Count > 0 ? summary[0] : new VoiceSummary(CallVoices.WholeOtherSide, TimeSpan.Zero, 1, []);
    }

    /// <summary>Какие карточки голосов открыты для смены имени.</summary>
    private readonly HashSet<string> _renaming = [];

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

    /// <summary>Карточка одного голоса.</summary>
    /// <param name="voice">Сводка: доля речи и цитаты.</param>
    /// <param name="index">Порядок появления — от него цвет.</param>
    /// <param name="name">Имя, если известно.</param>
    /// <param name="single">Голос на той стороне один: доля речи и «тот же человек» не нужны.</param>
    /// <param name="pick">Что сделать с выбранным именем.</param>
    private Border VoiceCard(VoiceSummary voice, int index, string? name, bool single, Action<string?> pick)
    {
        var body = new StackPanel();

        // Заголовок: цвет, имя, сколько говорил.
        var title = new Grid();
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        title.Children.Add(Dot(VoicePalette.For(index), 10));

        TextBlock nameText = name is null
            ? Ui.BodySecondary(single
                ? L.S.VoiceTheOtherSide
                : string.Format(L.S.Formatting, L.S.VoiceUnnamed, $"{L.S.TranscriptVoice} {voice.Id}"))
            : Ui.BodyStrong(name);
        nameText.TextWrapping = TextWrapping.NoWrap;
        nameText.TextTrimming = TextTrimming.CharacterEllipsis;
        nameText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(nameText, 1);
        title.Children.Add(nameText);

        TextBlock stats = Ui.Caption(single
            ? L.S.Duration(voice.Speech)
            : $"{L.S.Duration(voice.Speech)} · {Math.Round(voice.Share * 100).ToString(L.S.Formatting)}%");
        stats.Margin = new Thickness(Tokens.Space2, 0, 0, 0);
        stats.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(stats, 2);
        title.Children.Add(stats);
        body.Children.Add(title);

        if (!single)
        {
            body.Children.Add(ShareBar(voice.Share, VoicePalette.For(index)));
        }

        if (_rejected.GetValueOrDefault(voice.Id) >= 2 && !single)
        {
            body.Children.Add(RejectedHint());
        }

        if (name is null && _suggestions.TryGetValue(voice.Id, out VoiceMatch? suggested))
        {
            body.Children.Add(SuggestionRow(suggested, () => pick(suggested.Name)));
        }

        if (voice.Quotes.Count > 0)
        {
            var quotes = new StackPanel { Margin = new Thickness(0, Tokens.Space3, 0, 0) };
            foreach (CallLine quote in voice.Quotes)
            {
                quotes.Children.Add(QuoteRow(quote, voice.Id));
            }

            body.Children.Add(quotes);
        }

        // Имя уже есть — выбор свёрнут в «Изменить»: шесть чипов под
        // названным человеком — это шум, а не помощь.
        if (name is not null && !_renaming.Contains(voice.Id))
        {
            Button change = Ui.Link(L.S.VoiceChange);
            change.Margin = new Thickness(0, Tokens.Space1, 0, 0);
            change.HorizontalAlignment = HorizontalAlignment.Left;
            change.Click += (_, _) =>
            {
                _renaming.Add(voice.Id);
                ShowVoices();
            };
            body.Children.Add(change);
        }
        else
        {
            TextBlock who = Ui.Caption(L.S.VoiceWho);
            who.Margin = new Thickness(0, Tokens.Space2, 0, Tokens.Space2);
            body.Children.Add(who);
            body.Children.Add(NameChips(name, picked =>
            {
                _renaming.Remove(voice.Id);
                pick(picked);
            }));

            if (!single)
            {
                body.Children.Add(MergeLink(voice.Id));
            }
        }

        Border card = Ui.Card(body);
        card.Margin = new Thickness(0, 0, 0, Tokens.Space2);

        // Голос ждёт имени — рамка акцентом: это то, ради чего сюда пришли.
        if (name is null && !single)
        {
            card.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
        }

        return card;
    }

    /// <summary>«Это тот же человек, что…» — склеить голоса.</summary>
    private Button MergeLink(string voice)
    {
        Button merge = Ui.Link(L.S.VoiceSameAs);
        merge.Margin = new Thickness(0, Tokens.Space1, 0, 0);
        merge.HorizontalAlignment = HorizontalAlignment.Left;
        merge.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = merge };
            foreach (string other in _transcript!.Voices.Where(v => v != voice))
            {
                string otherName = CallSpeakers.NameOf(_session!, other) ?? $"{L.S.TranscriptVoice} {other}";
                var item = new MenuItem { Header = otherName };
                item.Click += (_, _) => MergeVoices(voice, other);
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        };

        return merge;
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
    private void ShowNotThisMenu(FrameworkElement anchor, CallLine quote, string voice)
    {
        if (_transcript is null || _session is null)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        IReadOnlyList<string> voices = _transcript.Voices;

        if (voices.Count == 0)
        {
            menu.Items.Add(SplitOrGetModelItem(_session.Directory));
        }
        else
        {
            foreach (string other in voices.Where(v => v != voice))
            {
                string target = other;
                var item = new MenuItem
                {
                    Header = CallSpeakers.NameOf(_session, other) ?? $"{L.S.TranscriptVoice} {other}",
                    Icon = new System.Windows.Shapes.Ellipse
                    {
                        Width = 9,
                        Height = 9,
                        Fill = VoicePalette.For(IndexOf(voices, other)),
                    },
                };
                item.Click += (_, _) => RejectQuote(quote, voice, target);
                menu.Items.Add(item);
            }

            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }

            // Новый голос — следующая свободная буква: реплика станет
            // отдельной карточкой, которую можно назвать.
            string fresh = CallVoices.Letter(voices.Count);
            for (int i = voices.Count; voices.Contains(fresh); i++)
            {
                fresh = CallVoices.Letter(i + 1);
            }

            var someone = new MenuItem { Header = L.S.VoiceSomeoneElse };
            someone.Click += (_, _) => RejectQuote(quote, voice, fresh);
            menu.Items.Add(someone);
        }

        menu.IsOpen = true;
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

    private void RejectQuote(CallLine quote, string voice, string target)
    {
        _rejected[voice] = _rejected.GetValueOrDefault(voice) + 1;
        ReassignLine(quote, target);
    }

    private Grid QuoteRow(CallLine quote, string voice)
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
        notThis.Click += (_, _) => ShowNotThisMenu(notThis, quote, voice);

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
    /// Сначала отмеченные на карточке после звонка, потом знакомые — свежие
    /// первыми. Искать нужное имя среди шестидесяти незачем: на звонке были
    /// те, кого отметили, а если нет — «другое…» открывает поле.
    /// </remarks>
    private WrapPanel NameChips(string? current, Action<string?> pick)
    {
        var chips = new WrapPanel();
        Style style = Ui.ChipStyle();

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

        // Подсказанные книгой голосов — первыми: их и выберут чаще всего.
        foreach (string hinted in _suggestions.Values.Select(m => m.Name).Reverse())
        {
            if (!names.Contains(hinted, StringComparer.OrdinalIgnoreCase))
            {
                names.Insert(0, hinted);
            }
        }

        foreach (string name in names)
        {
            bool selected = string.Equals(name, current, StringComparison.OrdinalIgnoreCase);
            var chip = new ToggleButton
            {
                Content = name,
                IsChecked = selected,
                Style = style,
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
    /// Карточка своей дорожки — такая же, как у голосов собеседников.
    /// </summary>
    /// <remarks>
    /// Здесь была подпись мелким шрифтом под карточками: «keshon — your
    /// microphone, 3 min». Голоса стоят карточками, а себя человек находил
    /// строчкой без рамки и не понимал, что она значит. Теперь это такая же
    /// карточка с тем же заголовком — цвет, имя, сколько говорил, — и прямым
    /// текстом: это вы, называть некого.
    /// </remarks>
    private Border MeCard()
    {
        TimeSpan mine = TimeSpan.FromSeconds(_transcript!.Lines
            .Where(l => l.Channel == CallChannel.Mine)
            .Sum(l => Math.Max(0, (l.End - l.Start).TotalSeconds)));

        var title = new Grid();
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        title.Children.Add(Dot(VoicePalette.Me, 10));

        TextBlock name = Ui.BodyStrong(Settings.EffectiveMyName);
        name.TextWrapping = TextWrapping.NoWrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        title.Children.Add(name);

        TextBlock stats = Ui.Caption(L.S.Duration(mine));
        stats.Margin = new Thickness(Tokens.Space2, 0, 0, 0);
        stats.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(stats, 2);
        title.Children.Add(stats);

        TextBlock hint = Ui.Caption(L.S.VoiceMe);
        hint.Margin = new Thickness(0, Tokens.Space2, 0, 0);

        Border card = Ui.Card(new StackPanel { Children = { title, hint } });
        card.Margin = new Thickness(0, 0, 0, Tokens.Space2);
        return card;
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
        var panel = new StackPanel { Margin = new Thickness(0, Tokens.Space5, 0, 0) };

        // Свёрнуто в ссылку: нужно редко, а в развёрнутом виде ряд цифр без
        // объяснения читался загадкой.
        if (!_resplitOpen)
        {
            Button open = Ui.Link(L.S.VoicesResplitLink);
            open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Click += (_, _) =>
            {
                _resplitOpen = true;
                ShowVoices();
            };
            panel.Children.Add(open);
            return panel;
        }

        // Выбор числа и отдельная кнопка — а не ряд цифр, где нажатие на
        // цифру сразу запускало разделение: передумать было нельзя, а
        // промахнуться — легко. «Отмена» сворачивает всё обратно в ссылку.
        TextBlock hint = Ui.Caption(L.S.VoicesResplitCount);
        hint.Margin = new Thickness(0, 0, 0, Tokens.Space1);
        panel.Children.Add(hint);

        int found = _transcript!.Voices.Count;
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
        LearnVoice(voice, name);
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
        _ = _services.RefreshPrints(directory);
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

            string fresh = CallVoices.Letter(transcript.Voices.Count);
            for (int i = transcript.Voices.Count; transcript.Voices.Contains(fresh); i++)
            {
                fresh = CallVoices.Letter(i + 1);
            }

            var someone = new MenuItem { Header = L.S.VoiceSomeoneElse };
            someone.Click += (_, _) => ReassignLine(row.Line, fresh);
            menu.Items.Add(someone);
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
            var fix = new MenuItem { Header = L.S.LineFixWord, Icon = new SymbolIcon { Symbol = SymbolRegular.TextEditStyle24 } };
            fix.Click += (_, _) => FixWordAt(box, row, box.SelectionLength > 0 ? box.SelectionStart : box.CaretIndex);
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
            FixWordAt(box, row, box.GetCharacterIndexFromPoint(e.GetPosition(box), snapToText: true));
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
    private void FixWordAt(TextBox box, TranscriptLineRow row, int index)
    {
        if (_transcript is null || _session is null || TranscriptEdit.WordAt(row.Line.Text, index) is not { } word)
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

        field.KeyDown += (_, e) =>
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

        popup.Opened += (_, _) =>
        {
            field.Focus();
            field.SelectAll();
        };
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
