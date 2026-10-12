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
    private Border BuildVoiceCard(CallPerson person, IReadOnlyList<CallPerson> people, bool busy)
    {
        string key = KeyOf(person);
        string display = person.IsMe ? Settings.EffectiveMyName : person.Name
            ?? SpeakerText("Voice ", "Голос ") + string.Join(", ", person.Voices);
        var body = new StackPanel();
        body.Children.Add(Ui.BodyStrong(display));
        if (person.IsMe)
        {
            body.Children.Add(Ui.Caption(L.S.VoiceMe));
            return Ui.Card(body);
        }
        var quotes = person.Quotes.Take(2).ToList();
        foreach (var line in quotes)
        {
            var play = new Wpf.Ui.Controls.Button { Content = SpeakerText("Listen · ", "Послушать · ") + CallTranscriptRenderer.Stamp(line.Start),
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Play16 },
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 4) };
            Controls.ControlSizing.Apply(play);
            play.Click += (_, _) => PlayQuote(line);
            _quoteButtons[line] = play;
            body.Children.Add(play);
            body.Children.Add(new TextBlock { Text = line.Text, TextWrapping = TextWrapping.Wrap });
        }
        if (quotes.Count == 0) body.Children.Add(Ui.Caption(SpeakerText("No speech is assigned yet.", "Реплики ещё не назначены.")));
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
        var fix = Ui.Link(SpeakerText("Fix assignments", "Исправить подписи"));
        fix.IsEnabled = !busy;
        fix.Margin = new Thickness(12, 0, 0, 0);
        fix.Click += (_, _) => { if (!_expanded.Remove(key)) _expanded.Add(key); ShowVoices(); };
        actions.Children.Add(fix);
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
            body.Children.Add(save);
            var cancel = Ui.Link(SpeakerText("Cancel", "Отмена"));
            cancel.Click += (_, _) => { _renaming.Remove(key); ShowVoices(); };
            body.Children.Add(cancel);
            body.Children.Add(Ui.Caption(SpeakerText("Changes the name across this call. Corrected phrases stay assigned to their people.",
                "Меняет имя в этом звонке. Исправленные реплики остаются у своих говорящих.")));
        }
        if (_expanded.Contains(key))
        {
            var split = new Button { Content = SpeakerText("Different people are speaking here", "Здесь говорят разные люди"),
                Margin = new Thickness(0, 10, 0, 0), IsEnabled = _services.AnalyseSpeakers is not null };
            split.Click += (_, _) => AnalyseSpeakerGroups(person.Voices);
            body.Children.Add(split);
            body.Children.Add(Ui.Caption(SpeakerText("The app proposes a separation for this voice only. Listen and name the resulting voices before applying.",
                "Приложение предложит разделение только этого голоса. Послушайте и подпишите новые голоса перед применением.")));
            if (quotes.FirstOrDefault() is { } example)
            {
                var correct = new Button { Content = SpeakerText("Correct this sample", "Исправить этот образец"), Margin = new Thickness(0, 8, 0, 0) };
                correct.Click += (_, _) =>
                {
                    var row = _lines.FirstOrDefault(r => r.Line.IsSameAs(example));
                    if (row is not null) Controls.AnchoredMenu.Open(LineMenu(row), correct);
                };
                body.Children.Add(correct);
            }
        }
        var card = Ui.Card(body);
        card.Margin = new Thickness(0, 0, 0, 12);
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
