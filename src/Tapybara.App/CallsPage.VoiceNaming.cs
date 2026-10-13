using System.Windows;
using System.Windows.Controls;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Tapybara.App;

public partial class CallsPage
{
    private static string SpeakerText(string en, string ru) =>
        L.S.Formatting.TwoLetterISOLanguageName == "ru" ? ru : en;

    private static bool HasMixedSpeech(CallLine line) =>
        line.SpeechMix != Tapybara.Core.Speech.SpeechMix.None;

    private readonly Dictionary<string, bool> _voiceCardsOpen = [];

    private Border BuildVoiceCard(CallPerson person, IReadOnlyList<CallPerson> people, bool busy)
    {
        string key = KeyOf(person);
        string display = person.IsMe ? Settings.EffectiveMyName : person.Name
            ?? SpeakerText("Voice ", "Голос ") + string.Join(", ", person.Voices);
        var body = new StackPanel();
        var heading = new Grid();
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nameRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var dot = MenuDot(ColorOf(person));
        dot.Margin = new Thickness(0, 0, 8, 0);
        nameRow.Children.Add(dot);
        var nameText = Ui.BodyStrong(display);
        nameText.TextTrimming = TextTrimming.CharacterEllipsis;
        nameRow.Children.Add(nameText);
        heading.Children.Add(nameRow);
        var stats = Ui.Caption(L.S.Duration(person.Speech) + " · " + Math.Round(person.Share * 100).ToString(L.S.Formatting) + "%");
        stats.ToolTip = SpeakerText("Speaking time · share of all speech", "Время речи · доля всей речи");
        stats.Margin = new Thickness(8, 0, 0, 0);
        stats.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(stats, 1);
        heading.Children.Add(stats);
        body.Children.Add(heading);
        body.Children.Add(new System.Windows.Controls.ProgressBar { Minimum = 0, Maximum = 1, Value = person.Share,
            Height = 4, Foreground = ColorOf(person), Margin = new Thickness(0, 10, 0, 8), IsHitTestVisible = false });
        if (person.IsMe && _session?.ImportedMedia != true)
        {
            body.Children.Add(Ui.Caption(L.S.VoiceMe));
            var mine = Ui.Card(body);
            mine.Margin = new Thickness(0, 12, 0, 0);
            return mine;
        }
        bool expanded = _voiceCardsOpen.GetValueOrDefault(key, person.Name is null);
        var details = new StackPanel { Visibility = expanded ? Visibility.Visible : Visibility.Collapsed };
        var toggle = new Wpf.Ui.Controls.Button {
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = expanded ? Wpf.Ui.Controls.SymbolRegular.ChevronUp16 : Wpf.Ui.Controls.SymbolRegular.ChevronDown16 },
            Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent,
            ToolTip = SpeakerText("Show or hide voice samples", "Показать или скрыть образцы голоса"),
            Margin = new Thickness(8, 0, 0, 0) };
        Controls.ControlSizing.Apply(toggle, iconOnly: true);
        System.Windows.Automation.AutomationProperties.SetName(toggle, display + " · " + toggle.ToolTip);
        toggle.Click += (_, _) => {
            expanded = !expanded;
            _voiceCardsOpen[key] = expanded;
            details.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            toggle.Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = expanded ? Wpf.Ui.Controls.SymbolRegular.ChevronUp16 : Wpf.Ui.Controls.SymbolRegular.ChevronDown16 };
        };
        Grid.SetColumn(toggle, 2);
        heading.Children.Add(toggle);
        body.Children.Add(details);
        var quotes = person.Quotes.Where(l => !HasMixedSpeech(l)).Take(3).ToList();
        foreach (var line in quotes)
        {
            var sample = new Grid { Margin = new Thickness(0, 10, 0, 4) };
            sample.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sample.ColumnDefinitions.Add(new ColumnDefinition());
            var play = new Wpf.Ui.Controls.Button {
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Play16 },
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 10, 0),
                ToolTip = SpeakerText("Play sample · ", "Послушать образец · ") + CallTranscriptRenderer.Stamp(line.Start) };
            Controls.ControlSizing.Apply(play, iconOnly: true);
            play.VerticalAlignment = VerticalAlignment.Top;
            play.CornerRadius = new CornerRadius(play.Height / 2);
            System.Windows.Automation.AutomationProperties.SetName(play, play.ToolTip.ToString());
            play.Click += (_, _) => PlayQuote(line);
            _quoteButtons[line] = play;
            sample.Children.Add(play);
            var quoteBody = new StackPanel();
            quoteBody.Children.Add(new TextBlock { Text = line.Text, TextWrapping = TextWrapping.Wrap });
            var quoteActions = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            var stamp = Ui.Caption(CallTranscriptRenderer.Stamp(line.Start));
            stamp.VerticalAlignment = VerticalAlignment.Center;
            quoteActions.Children.Add(stamp);
            var correct = Ui.SmallLink(SpeakerText("Change speaker", "Изменить говорящего"));
            correct.Margin = new Thickness(12, 0, 0, 0);
            correct.IsEnabled = !busy;
            correct.Click += (_, _) => {
                var row = _lines.FirstOrDefault(r => r.Line.IsSameAs(line));
                if (row is not null)
                {
                    Controls.AnchoredMenu.Open(LineMenu(row), correct);
                }
            };
            quoteActions.Children.Add(correct);
            quoteBody.Children.Add(quoteActions);
            Grid.SetColumn(quoteBody, 1);
            sample.Children.Add(quoteBody);
            details.Children.Add(sample);
        }
        if (quotes.Count == 0)
        {
            details.Children.Add(Ui.Caption(SpeakerText("No clean voice sample available.", "Нет чистого образца голоса.")));
        }

        if (person.Name is null && person.Voices.Count > 0
            && _suggestions.TryGetValue(person.Voices[0], out var suggestion))
        {
            var accept = new Button { Content = SpeakerText("Use ", "Подтвердить: ") + suggestion.Name,
                IsEnabled = !busy, Margin = new Thickness(0, 10, 0, 0) };
            accept.Click += (_, _) => SaveVoiceName(person, suggestion.Name);
            details.Children.Add(accept);
        }
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var rename = Ui.Link(person.Name is null ? SpeakerText("Choose name", "Выбрать имя") : SpeakerText("Rename person", "Переименовать"));
        rename.IsEnabled = !busy;
        rename.Click += (_, _) => { if (!_renaming.Remove(key)) { _renaming.Add(key); } ShowVoices(); };
        if (!person.IsMe)
        {
            actions.Children.Add(rename);
        }

        if (person.Voices.Any(v => _transcript!.Lines.Any(l => l.Voice == v
            && l.HumanEdits.HasFlag(CallLineEdits.Speaker) && !HasMixedSpeech(l) && l.End - l.Start >= VoiceCheck.MinLine)))
        {
            var find = Ui.Link(SpeakerText("Find similar speech…", "Найти похожую речь…"));
            find.Margin = new Thickness(actions.Children.Count > 0 ? 12 : 0, 0, 0, 0);
            find.IsEnabled = !busy;
            find.Click += async (_, _) => await FindPossiblePhrasesAsync(person);
            actions.Children.Add(find);
        }
        int possible = _transcript!.Lines.Count(l => l.SuggestedVoice is { } v && person.Voices.Contains(v)
            && !l.HumanEdits.HasFlag(CallLineEdits.Speaker) && !HasMixedSpeech(l));
        if (possible > 0)
        {
            var review = Ui.Link(SpeakerText($"Review matches ({possible})", $"Проверить совпадения ({possible})"));
            review.Margin = new Thickness(actions.Children.Count > 0 ? 12 : 0, 0, 0, 0);
            review.IsEnabled = !busy;
            review.Click += (_, _) => ReviewPossiblePhrases(person);
            actions.Children.Add(review);
        }
        details.Children.Add(actions);
        if (_renaming.Contains(key))
        {
            var input = new ComboBox { IsEditable = true, Text = person.Name ?? string.Empty,
                ItemsSource = people.Where(p => !p.IsMe && p.Name is not null).Select(p => p.Name!)
                    .Concat(_session!.Participants).Concat(Settings.KnownParticipants).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Margin = new Thickness(0, 8, 0, 0) };
            Controls.ControlSizing.Apply(input);
            details.Children.Add(Ui.Caption(SpeakerText("Choose a person or enter a name", "Выберите человека или введите имя")));
            details.Children.Add(input);
            var save = new Button { Content = SpeakerText("Save name", "Сохранить имя"), Margin = new Thickness(0, 8, 0, 0),
                IsEnabled = !string.IsNullOrWhiteSpace(input.Text) };
            input.AddHandler(System.Windows.Controls.TextBox.TextChangedEvent,
                new TextChangedEventHandler((_, _) => save.IsEnabled = !string.IsNullOrWhiteSpace(input.Text)));
            save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) { SaveVoiceName(person, input.Text.Trim()); } };
            var cancel = Ui.Link(SpeakerText("Cancel", "Отмена"));
            cancel.Click += (_, _) => { _renaming.Remove(key); ShowVoices(); };
            cancel.Margin = new Thickness(12, 8, 0, 0);
            details.Children.Add(new WrapPanel { Children = { save, cancel } });
        }

        var card = Ui.Card(body);
        card.Margin = new Thickness(0, 12, 0, 0);
        return card;
    }

    private async Task FindPossiblePhrasesAsync(CallPerson person)
    {
        if (_session is null || _services.RecheckSpeakers is null || _activeEditor?.Flush() == false)
        {
            return;
        }

        string directory = _session.Directory;
        var confirmation = new ConfirmWindow(SpeakerText("Find similar speech?", "Найти похожую речь?"),
            SpeakerText("Compare speech with confirmed samples. This may take several minutes. Review the suggestions before changing any assignments.",
                "Сравним речь с подтверждёнными образцами. Это может занять несколько минут. Подписи изменятся только после вашей проверки."),
            SpeakerText("Find matches", "Найти совпадения"), L.S.ButtonCancel);
        if (ConfirmWindow.Ask(Window.GetWindow(this), confirmation) != ConfirmChoice.Primary
            || _services.LiveState(directory) is not null)
        {
            return;
        }

        await _services.RecheckSpeakers(directory);
        if (_session?.Directory != directory)
        {
            return;
        }

        _transcript = CallTranscriptStore.Load(directory) ?? _transcript;
        RefreshAfterEdit();
        ReviewPossiblePhrases(person);
    }

    private void ReviewPossiblePhrases(CallPerson person)
    {
        if (_session is null || _transcript is null || _activeEditor?.Flush() == false)
        {
            return;
        }

        string directory = _session.Directory;
        var candidates = _transcript.Lines.Where(l => l.SuggestedVoice is { } v && person.Voices.Contains(v)
            && !l.HumanEdits.HasFlag(CallLineEdits.Speaker) && !HasMixedSpeech(l)).ToList();
        string name = person.IsMe ? Settings.EffectiveMyName : person.Name ?? person.Voices[0];
        var dialog = new ConfirmWindow(SpeakerText("Review matches", "Проверить совпадения"),
            candidates.Count == 0 ? SpeakerText("No reliable matches found. Short or mixed phrases may need manual correction.",
                "Надёжных совпадений не найдено. Короткие или смешанные реплики может потребоваться исправить вручную.")
                : SpeakerText("Listen, then select the phrases spoken by ", "Послушайте и выберите реплики: ") + name,
            SpeakerText("Apply selected", "Применить выбранные"), L.S.ButtonCancel) { PrimaryEnabled = false };
        var selections = new List<(CallLine Line, CheckBox Check)>();
        var rows = new StackPanel();
        foreach (var line in candidates)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            var check = new CheckBox { Content = CallTranscriptRenderer.Stamp(line.Start) + " · " + name };
            selections.Add((line, check));
            check.Checked += (_, _) => dialog.PrimaryEnabled = true;
            check.Unchecked += (_, _) => dialog.PrimaryEnabled = selections.Any(s => s.Check.IsChecked == true);
            row.Children.Add(check);
            var play = new Wpf.Ui.Controls.Button {
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Play16 },
                ToolTip = SpeakerText("Play phrase", "Послушать реплику"), Margin = new Thickness(0, 6, 0, 6) };
            Controls.ControlSizing.Apply(play, iconOnly: true);
            play.CornerRadius = new CornerRadius(play.Height / 2);
            System.Windows.Automation.AutomationProperties.SetName(play, play.ToolTip.ToString());
            play.Click += (_, _) => PlayQuote(line);
            _quoteButtons[line] = play;
            row.Children.Add(play);
            row.Children.Add(new TextBlock { Text = line.Text, TextWrapping = TextWrapping.Wrap });
            rows.Children.Add(row);
        }
        dialog.AddContent(new ScrollViewer { Content = rows, MaxHeight = 400,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var choice = ConfirmWindow.Ask(Window.GetWindow(this), dialog);
        foreach (var line in candidates)
        {
            _quoteButtons.Remove(line);
        }

        if (choice != ConfirmChoice.Primary || _session?.Directory != directory)
        {
            return;
        }

        var selected = selections.Where(s => s.Check.IsChecked == true).Select(s => s.Line).ToList();
        if (ExecuteEdit(directory, t =>
        {
            foreach (var old in selected)
            {
                var current = t.Lines.FirstOrDefault(l => l.IsSameAs(old));
                if (current is null || current.SuggestedVoice != old.SuggestedVoice || current.Text != old.Text
                    || current.Start != old.Start || current.End != old.End || HasMixedSpeech(current)
                    || current.HumanEdits.HasFlag(CallLineEdits.Speaker))
                {
                    continue;
                }

                t = CallVoices.Reassign(t, current, current.SuggestedVoice!);
            }
            return t;
        }))
        {
            RefreshAfterEdit();
            _ = _services.RefreshPrints(directory);
        }
    }

    private void SaveVoiceName(CallPerson person, string name)
    {
        if (_activeEditor?.Flush() == false || _session is null || _transcript is null)
        {
            return;
        }

        string directory = _session.Directory;
        if (person.Voices.Count == 0)
        {
            return;
        }

        string voice = IsWholeSide(person) ? CallVoices.NextFree(_transcript.Voices) : person.Voices[0];
        var voices = IsWholeSide(person) ? new[] { voice } : person.Voices;
        if (!ExecuteEdit(directory, t => SpeakerSeparation.AnchorNames(IsWholeSide(person)
            ? t with { Lines = t.Lines.Select(l => l.Channel == CallChannel.Theirs && l.Voice is null
                ? l with { Voice = voice } : l).ToList() } : t, voices.ToDictionary(v => v, _ => name)), metadataChange: s =>
            {
                var names = new Dictionary<string, string>(s.VoiceNames);
                foreach (var v in voices)
                {
                    names[v] = name;
                }

                return s with { VoiceNames = names };
            }))
        {
            return;
        }

        _renaming.Remove(KeyOf(person));
        RememberName(name);
        foreach (var v in voices)
        {
            LearnVoice(v, name);
        }

        RefreshAfterEdit();
    }
}
