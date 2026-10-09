using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Wpf.Ui.Controls;

using Brush = System.Windows.Media.Brush;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBox = System.Windows.Controls.TextBox;

namespace Tapybara.App;

/// <summary>Транскрипт: строки реплик, их цвета и меню «кто сказал».</summary>
public partial class CallsPage
{
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
        if (_activeEditor?.IsKeyboardFocusWithin == true || _activeEditor?.HasDraftChanges == true)
        {
            return;
        }
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
            bool named = (line.Channel == CallChannel.Mine && line.Voice is null)
                         || line.Voice is null
                         || CallSpeakers.NameOf(_session, line.Voice) is not null
                         || voices.Count <= 1;

            _lines.Add(new TranscriptLineRow
            {
                Line = line,
                Stamp = (line.TimingEstimated ? "~" : string.Empty) + CallTranscriptRenderer.Stamp(line.Start),
                Speaker = speaker,
                SpeakerBrush = BrushOf(line, colors),
                SpeakerForeground = named ? primary : secondary,
                SpeakerFontStyle = named ? FontStyles.Normal : FontStyles.Italic,
                SpeakerVisibility = speaker == previous && !line.HumanEdits.HasFlag(CallLineEdits.Structure) ? Visibility.Collapsed : Visibility.Visible,
                PlayHint = (line.TimingEstimated ? L.S.EditEstimated + "\n" : string.Empty)
                    + L.S.PlaybackFragment,
                IsCurrent = current == line.Start,
            });

            previous = speaker;
        }
    }

    /// <summary>Цвет реплики — цвет её человека: у голосов одного человека он общий.</summary>
    private static Brush BrushOf(CallLine line, IReadOnlyDictionary<string, int> colors)
    {
        if (line.Channel == CallChannel.Mine && line.Voice is null)
        {
            return VoicePalette.Me;
        }

        // Реплика без голоса — это собеседник, чьи голоса не разделялись:
        // цвет первого голоса, как у его карточки в панели.
        int color = line.Voice is { } voice && colors.TryGetValue(voice, out int known) ? known : 0;
        return color < 0 ? VoicePalette.Me : VoicePalette.For(color);
    }

    /// <summary>Отдать реплику другому голосу.</summary>
    /// <param name="line">Реплика.</param>
    /// <param name="voice">Голос.</param>
    /// <param name="name">Имя нового голоса — когда голос заводится ради этой реплики.</param>
    private void ReassignLine(CallLine line, string voice, string? name = null)
    {
        if (_activeEditor?.Flush() == false)
        {
            return;
        }
        if (_session is null)
        {
            return;
        }

        string directory = _session.Directory;
        if (!ExecuteEdit(directory, t => CallVoices.Reassign(t, line, voice), refresh: false,
            metadataChange: name is null ? null : s => s with
            {
                VoiceNames = new Dictionary<string, string>(s.VoiceNames) { [voice] = name },
            }))
        {
            return;
        }
        _activeEditor = null;
        RefreshAfterEdit();

        // Слепок голоса снят и с этой реплики — снять заново без неё, иначе
        // чужой голос остался бы в подсказках и лёг бы в книгу голосов.
        _ = _services.RefreshPrints(directory);
    }

    private void OnSpeakerClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TranscriptLineRow row } element)
        {
            ContextMenu menu = LineMenu(row);
            menu.PlacementTarget = element;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnLineContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is TextBox { DataContext: TranscriptLineRow row } box)
        {
            Controls.ContextMenuBehavior.Open(box, e, LineMenu(row, box));
        }
    }

    private void OnLineActionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TranscriptLineRow row } anchor)
        {
            TextBox? editor = anchor.Tag as TextBox;
            ContextMenu menu = LineMenu(row, editor);
            if (editor is not null) { editor.ContextMenu = menu; }
            Controls.AnchoredMenu.Open(menu, anchor);
        }
    }

    /// <summary>
    /// Меню реплики: кто её сказал, послушать, скопировать, исправить, удалить.
    /// </summary>
    /// <remarks>
    /// Источник аудио не меняется при назначении человека. Микрофон —
    /// подсказка по умолчанию, а не запрет на явную ручную коррекцию.
    /// </remarks>
    private ContextMenu LineMenu(TranscriptLineRow row, TextBox? box = null)
    {
        var menu = new ContextMenu();

        if (_transcript is not null && _session is not null)
        {
            menu.Items.Add(new MenuItem { Header = L.S.LineSaidBy, IsEnabled = false });
            AddSaidBy(menu, row.Line, reject: false);
            menu.Items.Add(new Separator());
        }

        var play = new MenuItem { Header = row.PlayHint, Icon = new SymbolIcon { Symbol = SymbolRegular.Play24 } };
        play.Click += (_, _) => PlayQuote(row.Line);
        menu.Items.Add(play);
        var freePlay = new MenuItem { Header = L.S.PlaybackFromLine, Icon = new SymbolIcon { Symbol = SymbolRegular.Play24 } };
        freePlay.Click += (_, _) => { SeekPlayback(row.Line.Start); if (!_player.IsPlaying) { TogglePlayback(); } };
        menu.Items.Add(freePlay);

        if (row.Line.Channel == CallChannel.Theirs && row.Line.Voice is not null)
        {
            var find = new MenuItem { Header = SpeakerText("Find similar speech…", "Найти похожие реплики…"),
                IsEnabled = row.Line.End - row.Line.Start >= Tapybara.Core.Calls.VoiceCheck.MinLine
                    && _services.FindSpeakerSpeech is not null };
            find.Click += (_, _) => FindSimilarSpeech(row.Line);
            menu.Items.Add(find);
        }

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
            AddEditingItems(menu, row, box);
        }

        menu.Items.Add(new Separator());
        var delete = new MenuItem { Header = L.S.LineDelete, Icon = new SymbolIcon { Symbol = SymbolRegular.Delete24 } };
        delete.Click += (_, _) => DeleteLine(row.Line);
        menu.Items.Add(delete);

        return menu;
    }

    /// <summary>Убрать реплику из транскрипта.</summary>
    /// <remarks>
    /// Без подтверждения: удаляют вздох или эхо, по одной строке подряд, и
    /// вопрос на каждую только мешал бы.
    /// </remarks>
    private void DeleteLine(CallLine line)
    {
        if (_activeEditor?.Flush() == false)
        {
            return;
        }
        if (_session is null)
        {
            return;
        }

        string directory = _session.Directory;
        if (!ExecuteEdit(directory, t => TranscriptEdit.RemoveLine(t, line)))
        {
            return;
        }

        // Слепок голоса снимался и с этой реплики.
        if (line.Voice is not null)
        {
            _ = _services.RefreshPrints(directory);
        }
    }

    // --- правка текста --------------------------------------------------------
}
