using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Wpf.Ui.Controls;

using AutomationProperties = System.Windows.Automation.AutomationProperties;
using Binding = System.Windows.Data.Binding;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Ellipse = System.Windows.Shapes.Ellipse;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MenuItem = System.Windows.Controls.MenuItem;
using Orientation = System.Windows.Controls.Orientation;
using TextBlock = System.Windows.Controls.TextBlock;
using TextElement = System.Windows.Documents.TextElement;
using UiButton = Wpf.Ui.Controls.Button;
using UiTextBox = Wpf.Ui.Controls.TextBox;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Tapybara.App;
public partial class CallsPage
{
    private Dictionary<string, VoiceMatch> _suggestions = [];
    private Dictionary<string, VoiceMatch> Suggest()
    {
        if (NeedsSpeakerCount)
        {
            return [];
        }

        var result = _transcript is not null && _session is not null
            ? new Dictionary<string, VoiceMatch>(SpeakerNameSuggestions.Find(_session, _transcript)) : new();
        if (!Settings.RememberVoices || _transcript is null || _session is null || NeedsSpeakerCount)
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
                result.TryAdd(voice, match);
            }
        }

        return result;
    }
    private void AcceptSuggestions()
    {
        foreach ((string voice, VoiceMatch match) in _suggestions.ToList())
        {
            if (People().FirstOrDefault(p => p.Voices.Contains(voice)) is { } person)
            {
                SaveVoiceName(person, match.Name);
            }
        }
    }

    private void LearnVoice(string voice, string? name)
    {
        if (name is not null
            && Settings.RememberVoices
            && _transcript?.VoicePrints.TryGetValue(voice, out float[]? print) == true)
        {
            _services.Voices.Learn(name, print);
        }
    }
    private IReadOnlyList<CallPerson> People() =>
        _transcript is null || _session is null ? [] : CallPeople.Of(_session, _transcript);

    private void ShowVoicesPane(bool visible)
    {
        visible &= _reviewVisible;
        _peopleRequested = visible;
        ArrangePanels();
    }
    private void ShowVoices()
    {
        VoicesHost.Children.Clear();
        _quoteButtons.Clear();
        if (!_reviewVisible)
        {
            ShowVoicesPane(false);
            return;
        }

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

        var header = new Grid();
        header.Children.Add(Ui.BodyStrong(NeedsSpeakerCount ? SpeakerText("Speakers", "Говорящие") : SpeakerText("People · ", "Люди · ") + people.Count));
        if (_services.SeparateSpeakers is not null)
        {
            var more = new UiButton { Content = "…", HorizontalAlignment = HorizontalAlignment.Right,
                ToolTip = SpeakerText("Voice options", "Действия с голосами") };
            Controls.ControlSizing.Apply(more);
            more.Click += (_, _) =>
            {
                var menu = new ContextMenu();
                var separate = new MenuItem { Header = SpeakerText("Redo speaker detection…", "Повторить определение говорящих…"),
                    IsEnabled = !busy && _services.CanSeparateSpeakers() };
                separate.Click += (_, _) => RetrySpeakerSeparation();
                menu.Items.Add(separate);
                Controls.AnchoredMenu.Open(menu, more);
            };
            header.Children.Add(more);
        }
        VoicesHost.Children.Add(header);
        VoicesHost.Children.Add(Ui.Caption(SpeakerText("Listen to a sample, then choose a name.", "Послушайте образец и выберите имя.")));
        if (NeedsSpeakerCount)
        {
            VoicesHost.Children.Add(Ui.Body(SpeakerText("Automatic speaker separation was too fragmented. Set the speaker count to retry; the transcript is preserved.",
                "Автоматическое разделение создало слишком много групп. Укажите число говорящих, чтобы повторить. Текст сохранён.")));
            var retry = new UiButton { Content = SpeakerText("Set speaker count…", "Указать число говорящих…"), Margin = new Thickness(0, 12, 0, 0),
                IsEnabled = !busy && _services.CanSeparateSpeakers() };
            retry.Click += (_, _) => RetrySpeakerSeparation();
            VoicesHost.Children.Add(retry);
        }
        else
        {
            foreach (CallPerson person in people)
            {
                VoicesHost.Children.Add(BuildVoiceCard(person, people, busy));
            }
        }
    }
    private readonly HashSet<string> _renaming = [];
    private readonly Dictionary<CallLine, UiButton> _quoteButtons = [];
    private void UpdateQuoteButtons()
    {
        foreach ((CallLine quote, UiButton button) in _quoteButtons)
        {
            bool playing = _playingQuote == quote && _player.IsPlaying;
            button.Icon = new SymbolIcon { Symbol = playing ? SymbolRegular.Stop16 : SymbolRegular.Play16 };
        }
    }
    private static string KeyOf(CallPerson person) =>
        person.IsMe ? CallSession.Me : person.Name?.ToUpperInvariant() ?? person.Voices[0];

    private static bool IsWholeSide(CallPerson person) => person.Voices is [CallVoices.WholeOtherSide];

    private static Brush ColorOf(CallPerson person) =>
        person.IsMe ? VoicePalette.Me : VoicePalette.For(Math.Max(person.Color, 0));

    private void ResetPanelState()
    {
        _renaming.Clear();
        _voiceCardsOpen.Clear();
        _quoteButtons.Clear();
    }

    private void AddSaidBy(ItemsControl menu, CallLine line)
    {
        foreach (CallPerson person in People())
        {
            bool current = (line.Voice is { } voice && person.Voices.Contains(voice))
                || (person.IsMe && line.Channel == CallChannel.Mine && line.Voice is null);

            string header = person switch
            {
                { IsMe: true } => Settings.EffectiveMyName,
                { Name: { } name } => name,
                _ => $"{L.S.TranscriptVoice} {person.Voices[0]}",
            };

            var item = new MenuItem
            {
                Header = header,
                IsCheckable = true,
                IsChecked = current,
                Icon = MenuDot(ColorOf(person)),
            };

            CallPerson target = person;
            item.Click += (_, _) =>
            {
                if (current)
                {
                    return;
                }

                // Своих голосов на той стороне может ещё не быть — тогда
                // реплика становится новым голосом, сразу названным «я».
                if (target.Voices.Count > 0)
                {
                    if (target.Voices[0] == CallVoices.WholeOtherSide)
                    {
                        ReassignLine(line, CallVoices.NextFree(_transcript!.Voices), target.Name);
                    }
                    else
                    {
                        ReassignLine(line, target.Voices[0]);
                    }
                }
                else
                {
                    ReassignLine(line, CallVoices.NextFree(_transcript!.Voices), CallSession.Me);
                }
            };
            menu.Items.Add(item);
        }

        if (_session?.ImportedMedia == true && !People().Any(p => p.IsMe))
        {
            var me = new MenuItem { Header = Settings.EffectiveMyName, Icon = MenuDot(VoicePalette.Me) };
            me.Click += (_, _) => ReassignLine(line, CallVoices.NextFree(_transcript!.Voices), CallSession.Me);
            menu.Items.Add(me);
        }
        menu.Items.Add(new Separator());

        // Новый голос — следующая свободная буква: реплика станет отдельной
        // карточкой «Кто это?».
        var someone = new MenuItem { Header = L.S.VoiceSomeoneElse };
        someone.Click += (_, _) =>
        {
            ReassignLine(line, CallVoices.NextFree(_transcript!.Voices));
        };
        menu.Items.Add(someone);

    }
    private static Ellipse MenuDot(Brush color)
    {
        Ellipse dot = Ui.Dot(color, Tokens.Dot);
        dot.Margin = new Thickness(0);
        return dot;
    }

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
}
