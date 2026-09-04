using System.Globalization;
using System.IO;
using System.Windows;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Tapybara.Core.Diagnostics;
using Wpf.Ui.Controls;

namespace Tapybara.App;

/// <summary>Чем закончилось окно разбора звонка.</summary>
public enum CallReviewOutcome
{
    /// <summary>Окно закрыли; участники и заметка сохранены.</summary>
    Closed,

    /// <summary>Запись не нужна — удалить папку целиком.</summary>
    Deleted,
}

/// <summary>
/// «Кто был на звонке?» — окно, которое превращает «Them» в имена.
/// </summary>
/// <remarks>
/// Один собеседник — его именем подписывается весь чужой канал, и никакого
/// разделения голосов не требуется вовсе. Несколько — имена становятся
/// подсказкой: разделителю голосов заранее известно, сколько их искать, а это
/// самый сильный рычаг точности, какой у такой задачи бывает.
/// <para>
/// Отмеченные имена сохраняются при ЛЮБОМ закрытии, включая «Позже» и крестик.
/// Заметка — только по «Сохранить». Разница намеренная: чипы накликиваются
/// мимоходом и терять их обидно, а недописанный текст сохранять как готовый
/// нельзя.
/// </para>
/// </remarks>
public partial class CallReviewWindow : FluentWindow
{
    private readonly CallSession _session;

    private bool _saved;

    public CallReviewWindow(CallSession session, string myName, IReadOnlyList<string> known)
    {
        InitializeComponent();

        _session = session;

        Participants.Load(myName, known, session.Participants);
        Participants.SelectionChanged += UpdateHint;

        NoteBox.Text = ReadNote(session.Directory);

        ApplyLanguage();
        Closing += (_, _) => Persist();
    }

    /// <summary>Что решил пользователь.</summary>
    public CallReviewOutcome Outcome { get; private set; } = CallReviewOutcome.Closed;

    /// <summary>Отмеченные участники на момент закрытия.</summary>
    public IReadOnlyList<string> SelectedParticipants { get; private set; } = [];

    /// <summary>Подставить надписи текущего языка.</summary>
    public void ApplyLanguage()
    {
        string title = Describe(_session);

        Title = L.S.CallReviewTitle;
        WindowTitleBar.Title = L.S.CallReviewTitle;
        HeadingText.Text = L.S.CallReviewHeading;
        SubheadingText.Text = title;
        ParticipantsLabel.Text = L.S.CallReviewParticipants;
        NoteLabel.Text = L.S.CallReviewNote;
        NoteBox.PlaceholderText = L.S.CallReviewNotePlaceholder;
        DeleteButton.Content = L.S.CallReviewDelete;
        LaterButton.Content = L.S.CallReviewLater;
        SaveButton.Content = L.S.CallReviewSave;

        Participants.ApplyLanguage();
        UpdateHint();
    }

    /// <summary>Подпись звонка: когда и откуда.</summary>
    private static string Describe(CallSession session)
    {
        string when = session.StartedAt.ToString("d MMMM, HH:mm", CultureInfo.CurrentCulture);
        string length = L.S.Duration(session.Duration);

        return string.IsNullOrWhiteSpace(session.Trigger)
            ? $"{when} · {length}"
            : $"{when} · {length} · {session.Trigger}";
    }

    /// <summary>
    /// Подсказка под чипами объясняет ПОСЛЕДСТВИЕ выбора, а не сам выбор.
    /// </summary>
    /// <remarks>
    /// «Отметьте участников» ничего не сообщает: и так видно, что это чипы.
    /// А вот что один собеседник распознается быстрее, чем трое, — знание,
    /// из-за которого человек и правда отметит имена.
    /// </remarks>
    private void UpdateHint() =>
        ParticipantsHint.Text = Participants.Selected.Count switch
        {
            0 => L.S.CallReviewHintNone,
            1 => L.S.CallReviewHintOne,
            _ => string.Format(CultureInfo.CurrentCulture, L.S.CallReviewHintMany, Participants.Selected.Count),
        };

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _saved = true;
        Close();
    }

    private void OnLaterClick(object sender, RoutedEventArgs e) => Close();

    private void OnDeleteClick(object sender, RoutedEventArgs e)
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
        SelectedParticipants = Participants.Selected;

        CallMeta.Save(_session with { Participants = SelectedParticipants });

        if (_saved)
        {
            WriteNote(_session.Directory, NoteBox.Text);
        }
    }

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

    private static void WriteNote(string directory, string text)
    {
        string path = Path.Combine(directory, CallLibrary.NoteFileName);
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                // Пустая заметка — это отсутствие заметки, а не файл из одного
                // перевода строки. Иначе список звонков показывал бы значок
                // заметки там, где читать нечего.
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }

            Directory.CreateDirectory(directory);
            File.WriteAllText(path, text.Trim() + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Не удалось сохранить заметку о звонке.", ex);
        }
    }
}
