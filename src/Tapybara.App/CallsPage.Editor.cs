using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Tapybara.App.Controls;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using ContextMenu = System.Windows.Controls.ContextMenu;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBox = System.Windows.Controls.TextBox;

namespace Tapybara.App;

/// <summary>Единые команды правки, история и привязка нативного редактора к документу звонка.</summary>
public partial class CallsPage
{
    private readonly Dictionary<string, CallEditSession> _editSessions = new(StringComparer.OrdinalIgnoreCase);
    private bool _noteHistoryActive;
    private readonly InlineEditScope _editFocus = new();
    private InlineTextEditor? _activeEditor
    {
        get => _editFocus.Active;
        set
        {
            if (value is null)
            {
                _editFocus.TryClear();
            }
            else
            {
                _editFocus.TryActivate(value);
            }
        }
    }

    private void OnEditorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is InlineTextEditor editor)
        {
            editor.CommitRequested -= CommitEditor;
            editor.CommitRequested += CommitEditor;
            editor.TextChanged -= OnEditorDraftChanged;
            editor.TextChanged += OnEditorDraftChanged;
        }
    }

    private void OnEditorFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is InlineTextEditor editor && _session is not null)
        {
            SmoothScroll.Cancel(LineList);
            if (!_editFocus.TryActivate(editor))
            {
                _activeEditor?.Focus();
                return;
            }
            _noteHistoryActive = false;
            editor.Tag = _session.Directory;
            UpdateHistory();
        }
    }

    private void OnEditorDraftChanged(object sender, TextChangedEventArgs e) => UpdateHistory();

    private bool CommitEditor(InlineTextEditor editor, object? context, string text)
    {
        if (context is not TranscriptLineRow row || editor.Tag is not string directory
            || !_editSessions.TryGetValue(directory, out CallEditSession? session))
        {
            return false;
        }
        CallLine? current = session.Document.Lines.FirstOrDefault(l => l.Id == row.Line.Id);
        if (current is null || current.Text != row.Line.Text
            || !ExecuteEdit(directory, t => TranscriptEdit.EditLine(t, row.Line, text), refresh: false))
        {
            ShowEditConflict();
            ShowBannerButton(L.S.EditKeepDraft, () =>
            {
                if (CallTranscriptStore.Load(directory) is not { } latest
                    || latest.Lines.FirstOrDefault(l => l.Id == row.Line.Id) is not { } latestLine)
                {
                    return;
                }
                session.Synchronize(latest);
                if (ExecuteEdit(directory, t => TranscriptEdit.EditLine(t, latestLine, text), refresh: false))
                {
                    editor.DiscardDraft();
                    _activeEditor = null;
                    RefreshAfterEdit();
                }
            });
            return false;
        }
        row.Line = session.Document.Lines.Single(l => l.Id == row.Line.Id);
        _detailStamp = string.Empty;
        return true;
    }

    private bool ExecuteEdit(string directory, Func<CallTranscript, CallTranscript> command, bool refresh = true,
        Func<CallSession, CallSession>? metadataChange = null)
    {
        if (!_editSessions.TryGetValue(directory, out CallEditSession? session))
        {
            ShowEditConflict();
            return false;
        }
        try
        {
            if (!session.Execute(command, metadataChange))
            {
                ShowEditConflict();
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Tapybara.Core.Diagnostics.AppLog.Warn("Не удалось сохранить правку транскрипта.", ex);
            ShowEditConflict();
            return false;
        }
        _services.Render(directory);
        if (_session?.Directory.Equals(directory, StringComparison.OrdinalIgnoreCase) == true)
        {
            _transcript = session.Document;
            _noteHistoryActive = false;
            UpdateHistory();
            if (refresh)
            {
                _activeEditor = null;
                RefreshAfterEdit();
            }
        }
        return true;
    }

    private void UpdateHistory()
    {
        if (_noteHistoryActive)
        {
            EditHistory.SetAvailability(NoteBox.CanUndo, NoteBox.CanRedo);
            return;
        }
        CallEditSession? session = _session is null ? null : _editSessions.GetValueOrDefault(_session.Directory);
        bool draft = _activeEditor?.HasDraftChanges == true;
        EditHistory.SetAvailability(session?.CanUndo == true || draft, session?.CanRedo == true && !draft);
    }

    private void RestoreHistory(bool redo)
    {
        if (_noteHistoryActive)
        {
            if (redo && NoteBox.CanRedo) { NoteBox.Redo(); }
            else if (!redo && NoteBox.CanUndo) { NoteBox.Undo(); }
            NoteBox.Focus();
            UpdateHistory();
            return;
        }
        if (_activeEditor?.Flush() == false)
        {
            return;
        }
        if (_session is null || !_editSessions.TryGetValue(_session.Directory, out CallEditSession? session))
        {
            return;
        }
        bool available = redo ? session.CanRedo : session.CanUndo;
        if (!available)
        {
            return;
        }
        if (!(redo ? session.Redo() : session.Undo()))
        {
            ShowEditConflict();
            return;
        }
        _services.Render(_session.Directory);
        _activeEditor = null;
        RefreshAfterEdit();
        UpdateHistory();
    }

    private void OnDocumentKey(object sender, KeyEventArgs e)
    {
        if (ImportPane.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape && !ImportLanguage.IsDropDownOpen)
            {
                OnCloseImport(sender, e);
                e.Handled = true;
            }
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.F or Key.H && _transcript is not null)
        {
            e.Handled = true;
            OpenDocumentSearch(replace: e.Key == Key.H);
            return;
        }
        if (_activeEditor?.IsKeyboardFocusWithin != true && !LineList.IsKeyboardFocusWithin)
        {
            return;
        }
        if (e.Key == Key.Z && Keyboard.Modifiers is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            RestoreHistory(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            RestoreHistory(redo: true);
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control
                 && _activeEditor is { DataContext: TranscriptLineRow row } editor)
        {
            e.Handled = true;
            SplitAt(row, editor);
        }
    }

    private void SplitAt(TranscriptLineRow row, TextBox box)
    {
        int caret = box.CaretIndex;
        if (_activeEditor?.Flush() == false)
        {
            return;
        }
        if (_session is null || !CallDocumentCommands.CanSplit(row.Line.Text, caret) || box.SelectionLength != 0)
        {
            return;
        }
        ExecuteEdit(_session.Directory, t => CallDocumentCommands.Split(t, row.Line.Id, caret));
    }

    private void AddEditingItems(ContextMenu menu, TranscriptLineRow row, TextBox box)
    {
        if (box is InlineTextEditor { HasDraftChanges: true } draft)
        {
            var discard = new MenuItem { Header = L.S.EditDiscardDraft };
            discard.Click += (_, _) =>
            {
                draft.DiscardDraft();
                _activeEditor = null;
                RefreshAfterEdit();
            };
            menu.Items.Add(discard);
        }
        var split = new MenuItem
        {
            Header = L.S.EditSplit, InputGestureText = "Ctrl+Enter",
            IsEnabled = box.SelectionLength == 0 && CallDocumentCommands.CanSplit(box.Text, box.CaretIndex),
        };
        split.Click += (_, _) => SplitAt(row, box);
        menu.Items.Add(split);
        List<CallLine> ordered = _transcript?.Lines.ToList() ?? [];
        int index = ordered.FindIndex(l => l.Id == row.Line.Id);
        foreach (bool upwards in new[] { true, false })
        {
            int neighborIndex = index + (upwards ? -1 : 1);
            CallLine? neighbor = index >= 0 && neighborIndex >= 0 && neighborIndex < ordered.Count ? ordered[neighborIndex] : null;
            var merge = new MenuItem
            {
                Header = upwards ? L.S.EditMergePrevious : L.S.EditMerge,
                IsEnabled = neighbor is not null && neighbor.Channel == row.Line.Channel && neighbor.Voice == row.Line.Voice,
                ToolTip = L.S.EditMergeHelp,
            };
            merge.Click += (_, _) =>
            {
                if (_activeEditor?.Flush() == false) { return; }
                if (_session is not null && neighbor is not null)
                {
                    ExecuteEdit(_session.Directory, t => CallDocumentCommands.Merge(t,
                        upwards ? neighbor.Id : row.Line.Id, upwards ? row.Line.Id : neighbor.Id));
                }
            };
            menu.Items.Add(merge);
        }
        var timing = new MenuItem { Header = L.S.EditTiming };
        timing.Click += (_, _) => OpenBoundaryEditor(box, row.Line, add: false);
        menu.Items.Add(timing);
        var add = new MenuItem { Header = L.S.EditAddSpeech };
        add.Click += (_, _) => OpenBoundaryEditor(box, row.Line, add: true);
        menu.Items.Add(add);

        if (box.SelectionLength > 0)
        {
            int start = box.SelectionStart;
            int length = box.SelectionLength;
            var deleteSelection = new MenuItem { Header = L.S.EditDeleteSelection };
            deleteSelection.Click += (_, _) =>
            {
                if (_activeEditor?.Flush() == false)
                {
                    return;
                }
                if (_session is not null)
                {
                    ExecuteEdit(_session.Directory, t => start == 0 && length == row.Line.Text.Length
                        ? TranscriptEdit.RemoveLine(t, row.Line)
                        : TranscriptEdit.ReplaceAt(t, row.Line, start, length, string.Empty));
                }
            };
            menu.Items.Add(deleteSelection);
            var assign = new MenuItem { Header = L.S.EditAssignSelection };
            foreach (CallPerson person in People())
            {
                var item = new MenuItem { Header = person.IsMe ? Settings.EffectiveMyName : person.Name ?? $"{L.S.TranscriptVoice} {person.Voices[0]}" };
                CallPerson target = person;
                item.Click += (_, _) =>
                {
                    if (_activeEditor?.Flush() == false)
                    {
                        return;
                    }
                    string voice = target.Voices.FirstOrDefault(v => v != CallVoices.WholeOtherSide)
                        ?? CallVoices.NextFree(_transcript!.Voices);
                    string? name = target.IsMe ? CallSession.Me : target.Name;
                    OpenBoundaryEditor(box, row.Line, add: false, selection: (start, length, voice, name));
                };
                assign.Items.Add(item);
            }
            menu.Items.Add(assign);
        }
        var undo = new MenuItem { Header = L.S.EditUndo, InputGestureText = "Ctrl+Z", IsEnabled = _session is not null && _editSessions.GetValueOrDefault(_session.Directory)?.CanUndo == true };
        undo.Click += (_, _) => RestoreHistory(redo: false);
        menu.Items.Add(undo);
        var redo = new MenuItem { Header = L.S.EditRedo, InputGestureText = "Ctrl+Shift+Z", IsEnabled = _session is not null && _editSessions.GetValueOrDefault(_session.Directory)?.CanRedo == true };
        redo.Click += (_, _) => RestoreHistory(redo: true);
        menu.Items.Add(redo);
    }

    private void OpenBoundaryEditor(FrameworkElement anchor, CallLine line, bool add,
        (int Start, int Length, string Voice, string? Name)? selection = null,
        (TimeSpan Start, TimeSpan End)? proposed = null)
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
        TimeSpan maximum = (line.Channel == CallChannel.Mine ? _microphoneWave : _systemWave)?.Duration ?? TimeSpan.Zero;
        if (maximum <= TimeSpan.Zero) { maximum = PlayingSelectedCall ? _player.Duration : _session.Duration; }
        var editor = new AudioBoundaryEditor(proposed?.Start ?? line.Start, proposed?.End ?? line.End, maximum,
            add ? string.Empty : null,
            selection is { } s ? (from, to) => from >= line.Start && to <= line.End
                && (s.Start == 0 || from > line.Start)
                && (s.Start + s.Length == line.Text.Length || to < line.End) : null,
            applyLabel: add ? L.S.EditInsertLine : L.S.EditApply,
            help: add ? L.S.EditAddSpeechHelp : L.S.EditBoundaryHelp,
            title: (add ? L.S.EditAddSpeech : selection is not null ? L.S.EditAssignSelection : L.S.EditTiming)
                + " · " + (_lines.FirstOrDefault(r => r.Line.Id == line.Id)?.Speaker ?? string.Empty));
        var card = new PopoverSurface { Child = editor, Padding = new Thickness(Tokens.Space4), CornerRadius = Tokens.CardRadius };
        var popup = new Popup { Child = card, PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        editor.PlayRequested += (from, to) =>
        {
            if (_current?.Directory.Equals(directory, StringComparison.OrdinalIgnoreCase) != true) { return; }
            _playingQuote = null;
            _playbackFailed = !_player.Play(directory, from, to, line.Channel);
            if (!_playbackFailed)
            {
                _playbackTimer.Start();
            }
            else { _playbackTimer.Stop(); }
            FollowPlayback();
            UpdateTransport();
        };
        editor.ApplyRequested += (from, to, text) =>
        {
            if (_current?.Directory.Equals(directory, StringComparison.OrdinalIgnoreCase) != true
                || _activeEditor?.Flush() == false) { return; }
            CallLine? current = _transcript?.Lines.FirstOrDefault(l => l.Id == line.Id);
            if (current is null || current.Start != line.Start || current.End != line.End
                || (selection is not null && current.Text != line.Text)) { ShowEditConflict(); return; }
            bool applied = ExecuteEdit(directory, t => selection is { } selected
                ? CallDocumentCommands.AssignSelection(t, line.Id, selected.Start, selected.Length, from, to, selected.Voice)
                : add ? CallDocumentCommands.Insert(t, line.Channel, from, to, text!, line.Voice)
                : CallDocumentCommands.SetTiming(t, line.Id, from, to), refresh: false,
                metadataChange: selection is { Name: not null } named ? meta => meta with
                {
                    VoiceNames = new Dictionary<string, string>(meta.VoiceNames) { [named.Voice] = named.Name },
                } : null);
            if (applied)
            {
                popup.IsOpen = false;
                _activeEditor = null;
                RefreshAfterEdit();
            }
        };
        popup.Closed += (_, _) =>
        {
            PageRoot.Children.Remove(popup);
            if (_wordFix == popup)
            {
                _wordFix = null;
            }
        };
        popup.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(card) is System.Windows.Interop.HwndSource source)
            {
                NativeFocus.Set(source.Handle);
            }
            editor.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        };
        CloseWordFix();
        PageRoot.Children.Add(popup);
        _wordFix = popup;
        AnchoredPopup.Open(popup, anchor, above: anchor == Timeline.TimingAnchor);
    }

    private void ShowEditConflict()
    {
        Banner.Visibility = Visibility.Visible;
        BannerText.Text = L.S.EditConflict;
    }
}
