using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
using Wpf.Ui.Controls;

using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ContextMenu = System.Windows.Controls.ContextMenu;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Tapybara.App;

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

        // У кнопок из одного значка подсказка — не имя: экранный диктор и
        // UI Automation видели бы безымянную кнопку.
        System.Windows.Automation.AutomationProperties.SetName(FolderButton, L.S.CallsOpenFolder);
        System.Windows.Automation.AutomationProperties.SetName(MoreButton, L.S.CallsMore);
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
            ResetPanelState();
            CloseWordFix();
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

        // Не сразу: название сохраняется, когда поле теряет фокус, — чаще всего
        // от щелчка по другому звонку. Пересобранный прямо сейчас список
        // заменил бы строку, по которой щёлкают, и щелчок пропал бы.
        Dispatcher.BeginInvoke(() => Reload(keepSelection: true), DispatcherPriority.Background);
    }

    /// <summary>Звонки, сверку которых уже попросили, — чтобы не просить на каждом обновлении.</summary>
    private readonly HashSet<string> _verifyAsked = new(StringComparer.OrdinalIgnoreCase);

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
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
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
