using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Wpf.Ui.Controls;

using ContextMenu = System.Windows.Controls.ContextMenu;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Tapybara.App;

/// <summary>Чем закончилась карточка звонка.</summary>
public enum CallReviewOutcome
{
    /// <summary>Карточку закрыли; название и участники сохранены.</summary>
    Closed,

    /// <summary>Запись не нужна — удалить папку целиком.</summary>
    Deleted,

    /// <summary>Открыть звонок в окне Tapybara.</summary>
    Opened,
}

/// <summary>
/// Карточка после звонка: как назвать и кто был — пока идёт распознавание.
/// </summary>
/// <remarks>
/// <para>
/// Один собеседник — его именем подписывается весь чужой канал, и никакого
/// разделения голосов не требуется вовсе. Несколько — это подсказка, сколько
/// голосов искать: самый сильный рычаг точности, какой у такой задачи бывает.
/// Кто из отмеченных какой голос — решает человек потом, по цитатам.
/// </para>
/// <para>
/// Всё сохраняется сразу и при любом закрытии. Раньше здесь была пара
/// «Позже» / «Сохранить», и «Позже» не откладывало ничего: распознавание
/// всё равно начиналось, просто без заметки.
/// </para>
/// </remarks>
public partial class CallReviewWindow : FluentWindow
{
    /// <summary>Отступ карточки от края рабочей области — как у уведомлений Windows.</summary>
    private const double EdgeMargin = 12;

    private readonly CallSession _session;

    public CallReviewWindow(CallSession session, string myName, IReadOnlyList<string> known)
    {
        InitializeComponent();

        _session = session;

        Participants.Load(myName, known, session.Participants);
        Participants.SelectionChanged += () =>
        {
            UpdateHint();

            // Сразу на диск, а не при закрытии: распознавание уже идёт и
            // прочитает участников из меты, когда дойдёт до разделения
            // голосов. Карточка к тому времени может быть ещё открыта.
            SaveParticipants();
        };

        TitleBox.Text = session.Title ?? string.Empty;

        ApplyLanguage();
        SetWaiting();

        Loaded += (_, _) => PlaceNearTray();
        Closing += (_, _) => Persist();
    }

    /// <summary>Что решил пользователь.</summary>
    public CallReviewOutcome Outcome { get; private set; } = CallReviewOutcome.Closed;

    /// <summary>Отмеченные участники на момент закрытия.</summary>
    public IReadOnlyList<string> SelectedParticipants { get; private set; } = [];

    /// <summary>Папка звонка, о котором карточка.</summary>
    public string CallDirectory => _session.Directory;

    /// <summary>Подставить надписи текущего языка.</summary>
    public void ApplyLanguage()
    {
        Title = L.S.CallReviewTitle;
        WindowTitleBar.Title = "Tapybara";
        HeadingText.Text = string.Format(L.S.Formatting, L.S.CardHeading, L.S.Duration(_session.Duration));
        SubheadingText.Text = Describe(_session);
        TitleLabel.Text = L.S.CardTitleField;
        TitleBox.PlaceholderText = L.S.CardTitlePlaceholder;
        ParticipantsLabel.Text = L.S.CardWho;
        OpenButton.Content = L.S.CardOpenCall;
        DoneButton.Content = L.S.CallReviewSave;
        MoreButton.ToolTip = L.S.CallsMore;

        Participants.ApplyLanguage();
        UpdateHint();
    }

    /// <summary>Звонок ждёт, пока распознается предыдущий.</summary>
    public void SetWaiting()
    {
        ProgressText.Text = L.S.CardWaiting;
        PercentText.Text = string.Empty;
        Progress.IsIndeterminate = true;
    }

    /// <summary>Как идёт распознавание этого звонка.</summary>
    public void SetProgress(CallTranscriptionProgress progress)
    {
        Progress.IsIndeterminate = false;
        Progress.Value = progress.Percent;
        ProgressText.Text = L.S.Describe(progress.Stage);
        PercentText.Text = $"{progress.Percent.ToString(L.S.Formatting)}%";
    }

    /// <summary>Распознавание закончилось.</summary>
    /// <param name="needsNames">Нашлось несколько голосов без имён.</param>
    public void SetFinished(bool needsNames)
    {
        Progress.IsIndeterminate = false;
        Progress.Value = 100;
        ProgressText.Text = needsNames ? L.S.CardNeedsNames : L.S.CardReady;
        PercentText.Text = string.Empty;

        // Теперь главное действие — открыть звонок: там транскрипт и голоса.
        OpenButton.Appearance = ControlAppearance.Primary;
        DoneButton.Appearance = ControlAppearance.Secondary;
    }

    /// <summary>Подпись звонка: когда и откуда.</summary>
    private static string Describe(CallSession session)
    {
        string when = L.S.Date(session.StartedAt, withTime: true);
        return string.IsNullOrWhiteSpace(session.Trigger) ? when : $"{when} · {session.Trigger}";
    }

    /// <summary>
    /// Поставить карточку в угол у трея.
    /// </summary>
    /// <remarks>
    /// Там, где Windows показывает уведомления, — то есть там, куда глаз
    /// смотрит после «записано». Раньше окно вставало посреди экрана, поверх
    /// того, чем человек был занят.
    /// </remarks>
    private void PlaceNearTray()
    {
        Rect area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - EdgeMargin;
        Top = area.Bottom - ActualHeight - EdgeMargin;
    }

    /// <summary>
    /// Подсказка под чипами объясняет ПОСЛЕДСТВИЕ выбора, а не сам выбор.
    /// </summary>
    /// <remarks>
    /// «Отметьте участников» ничего не сообщает: и так видно, что это чипы.
    /// А вот что один собеседник подпишется сразу, а несколько — назовутся
    /// по цитатам, — знание, из-за которого человек и правда отметит имена.
    /// </remarks>
    private void UpdateHint()
    {
        bool several = Participants.Selected.Count > 1;
        bool cannotSplit = several && _splitModelsMissing?.Invoke() == true;

        ParticipantsHint.Text = Participants.Selected.Count switch
        {
            0 => L.S.CardHintNone,
            1 => L.S.CallReviewHintOne,
            _ when cannotSplit => string.Format(L.S.Formatting, L.S.CardModelsMissing, _missingNames?.Invoke()),
            _ => string.Format(L.S.Formatting, L.S.CardHintMany, Participants.Selected.Count),
        };

        SplitModelsButton.Content = L.S.VoicesOpenModels;
        SplitModelsButton.Visibility = cannotSplit ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Нет ли моделей для разделения голосов.</summary>
    private Func<bool>? _splitModelsMissing;

    /// <summary>Каких именно — названиями групп на странице моделей.</summary>
    private Func<string>? _missingNames;

    /// <summary>Открыть страницу моделей.</summary>
    private Action? _openModels;

    /// <summary>
    /// Подсказать о недостающих моделях, если отмечено несколько человек.
    /// </summary>
    /// <remarks>
    /// Раньше карточка обещала «поищу столько голосов», а без моделей
    /// распознавание молча обходилось без разделения — и все собеседники
    /// оказывались одним голосом.
    /// </remarks>
    public void OfferSplitModels(Func<bool> missing, Func<string> missingNames, Action openModels)
    {
        _splitModelsMissing = missing;
        _missingNames = missingNames;
        _openModels = openModels;
        UpdateHint();
    }

    private void OnSplitModelsClick(object sender, RoutedEventArgs e) => _openModels?.Invoke();

    private void OnTitleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SaveTitle();
            Participants.Focus();
        }
    }

    private void OnDoneClick(object sender, RoutedEventArgs e) => Close();

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        Outcome = CallReviewOutcome.Opened;
        Close();
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = MoreButton, Placement = PlacementMode.Top };
        var delete = new MenuItem
        {
            Header = L.S.CallReviewDelete,
            Icon = new SymbolIcon { Symbol = SymbolRegular.Delete24 },
        };
        delete.Click += (_, _) => AskDelete();
        menu.Items.Add(delete);
        menu.IsOpen = true;
    }

    /// <summary>
    /// Удалить запись — из меню «⋯», а не кнопкой на виду.
    /// </summary>
    /// <remarks>
    /// Раньше «Удалить запись» стояла первой кнопкой в окне, которое
    /// выскакивает сразу после разговора. Разговор заново не случится, а
    /// промахнуться мимо «Готово» легко.
    /// </remarks>
    private void AskDelete()
    {
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

        Outcome = CallReviewOutcome.Deleted;
        Close();
    }

    /// <summary>
    /// Записать выбранное на диск.
    /// </summary>
    /// <remarks>
    /// Зовётся из <c>Closing</c>, то есть и на крестик тоже. Раньше подобные
    /// окна теряли накликанное при закрытии крестиком — потому что «крестик»
    /// казался отменой, хотя для пользователя это просто «убрать окно».
    /// </remarks>
    private void Persist()
    {
        if (Outcome == CallReviewOutcome.Deleted)
        {
            return; // папки сейчас не станет, писать в неё нечего
        }

        Participants.Flush();
        SaveParticipants();
        SaveTitle();
    }

    private void SaveParticipants()
    {
        SelectedParticipants = Participants.Selected;
        IReadOnlyList<string> selected = SelectedParticipants;
        CallMeta.Update(_session.Directory, s => s with { Participants = selected });
    }

    private void SaveTitle()
    {
        string text = TitleBox.Text.Trim();
        string? title = text.Length == 0 ? null : text;
        CallMeta.Update(_session.Directory, s => s.Title == title ? s : s with { Title = title });
    }
}
