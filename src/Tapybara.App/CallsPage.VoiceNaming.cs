using System.Windows;
using System.Windows.Controls;
using Tapybara.Core.Calls;
using Tapybara.App.Localization;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Tapybara.App;

public partial class CallsPage
{
    private static string SpeakerText(string en, string ru) =>
        L.S.Formatting.TwoLetterISOLanguageName == "ru" ? ru : en;

    private static bool HasMixedSpeech(CallLine line) =>
        line.SpeechMix != Tapybara.Core.Speech.SpeechMix.None;

    private Border BuildVoiceCard(CallPerson person, IReadOnlyList<CallPerson> people, bool busy)
    {
        string key = KeyOf(person);
        string display = person.IsMe ? Settings.EffectiveMyName : person.Name
            ?? SpeakerText("Voice ", "Голос ") + string.Join(", ", person.Voices);
        var body = new StackPanel();
        var heading = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var dot = MenuDot(ColorOf(person));
        dot.Margin = new Thickness(0, 0, 8, 0);
        heading.Children.Add(dot);
        heading.Children.Add(Ui.BodyStrong(display));
        body.Children.Add(heading);
        if (person.IsMe)
        {
            body.Children.Add(Ui.Caption(L.S.VoiceMe));
            var mine = Ui.Card(body);
            mine.Margin = new Thickness(0, 12, 0, 0);
            return mine;
        }
        var quotes = person.Quotes.Where(l => !HasMixedSpeech(l)).Take(2).ToList();
        foreach (var line in quotes)
        {
            var play = new Wpf.Ui.Controls.Button { Content = SpeakerText("Listen · ", "Послушать · ") + CallTranscriptRenderer.Stamp(line.Start),
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Play16 },
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 4) };
            Controls.ControlSizing.Apply(play);
            play.Click += (_, _) => PlayQuote(line);
            _quoteButtons[line] = play;
            body.Children.Add(play);
            body.Children.Add(new TextBlock { Text = line.Text, TextWrapping = TextWrapping.Wrap,
                MaxHeight = 60, TextTrimming = TextTrimming.WordEllipsis, Margin = new Thickness(0, 2, 0, 6), ToolTip = line.Text });
        }
        if (quotes.Count == 0) body.Children.Add(Ui.Caption(SpeakerText("No clean voice sample available.", "Нет чистого образца голоса.")));
        if (person.Name is null && person.Voices.Count > 0
            && _suggestions.TryGetValue(person.Voices[0], out var suggestion))
        {
            var accept = new Button { Content = SpeakerText("Use ", "Подтвердить: ") + suggestion.Name,
                IsEnabled = !busy, Margin = new Thickness(0, 10, 0, 0) };
            accept.Click += (_, _) => SaveVoiceName(person, suggestion.Name);
            body.Children.Add(accept);
        }
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var rename = Ui.Link(person.Name is null ? SpeakerText("Choose name", "Выбрать имя") : SpeakerText("Rename person", "Переименовать"));
        rename.IsEnabled = !busy;
        rename.Click += (_, _) => { if (!_renaming.Remove(key)) _renaming.Add(key); ShowVoices(); };
        actions.Children.Add(rename);
        body.Children.Add(actions);
        if (_renaming.Contains(key))
        {
            var input = new ComboBox { IsEditable = true, Text = person.Name ?? string.Empty,
                ItemsSource = people.Where(p => !p.IsMe && p.Name is not null).Select(p => p.Name!)
                    .Concat(_session!.Participants).Concat(Settings.KnownParticipants).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Margin = new Thickness(0, 8, 0, 0) };
            Controls.ControlSizing.Apply(input);
            body.Children.Add(Ui.Caption(SpeakerText("Choose a person or enter a name", "Выберите человека или введите имя")));
            body.Children.Add(input);
            var save = new Button { Content = SpeakerText("Save name", "Сохранить имя"), Margin = new Thickness(0, 8, 0, 0),
                IsEnabled = !string.IsNullOrWhiteSpace(input.Text) };
            input.AddHandler(System.Windows.Controls.TextBox.TextChangedEvent,
                new TextChangedEventHandler((_, _) => save.IsEnabled = !string.IsNullOrWhiteSpace(input.Text)));
            save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) SaveVoiceName(person, input.Text.Trim()); };
            var cancel = Ui.Link(SpeakerText("Cancel", "Отмена"));
            cancel.Click += (_, _) => { _renaming.Remove(key); ShowVoices(); };
            cancel.Margin = new Thickness(12, 8, 0, 0);
            body.Children.Add(new WrapPanel { Children = { save, cancel } });
        }

        var card = Ui.Card(body);
        card.Margin = new Thickness(0, 12, 0, 0);
        return card;
    }

    private void SaveVoiceName(CallPerson person, string name)
    {
        if (_activeEditor?.Flush() == false || _session is null || _transcript is null) return;
        string directory = _session.Directory;
        if (person.Voices.Count == 0) return;
        string voice = IsWholeSide(person) ? CallVoices.NextFree(_transcript.Voices) : person.Voices[0];
        var voices = IsWholeSide(person) ? new[] { voice } : person.Voices;
        if (!ExecuteEdit(directory, t => IsWholeSide(person)
            ? t with { Lines = t.Lines.Select(l => l.Channel == CallChannel.Theirs && l.Voice is null
                ? l with { Voice = voice } : l).ToList() } : t, metadataChange: s =>
            {
                var names = new Dictionary<string, string>(s.VoiceNames);
                foreach (var v in voices) names[v] = name;
                return s with { VoiceNames = names };
            })) return;
        _renaming.Remove(KeyOf(person));
        RememberName(name);
        foreach (var v in voices) LearnVoice(v, name);
        RefreshAfterEdit();
    }
}
