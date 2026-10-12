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
    private const int SuggestedNames = 6;
    private Dictionary<string, VoiceMatch> _suggestions = [];
    private Dictionary<string, VoiceMatch> Suggest()
    {
        var result = new Dictionary<string, VoiceMatch>();
        if (!Settings.RememberVoices || _transcript is null || _session is null)
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
                result[voice] = match;
            }
        }

        return result;
    }
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
                SetNames([voice], match.Name);
            }
        }
    }
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

        var confirm = new UiButton
        {
            Content = L.S.VoiceAcceptSuggestion,
            Appearance = ControlAppearance.Primary,
            Margin = new Thickness(0, Tokens.Space2, 0, 0),
        };
        confirm.Click += (_, _) => accept();
        row.Children.Add(confirm);
        return row;
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

        VoicesHost.Children.Add(Ui.BodyStrong(SpeakerText("Voices", "Голоса")));
        VoicesHost.Children.Add(Ui.Caption(SpeakerText("Listen to a sample, then choose a name.", "Послушайте образец и выберите имя.")));
        if (_services.RecheckSpeakers is not null)
        {
            var check = new UiButton { Content = busy ? SpeakerText("Working…", "Обработка…")
                : SpeakerText("Check voices", "Проверить голоса"), IsEnabled = !busy, Margin = new Thickness(0, 8, 0, 0) };
            check.ToolTip = SpeakerText("Checks existing audio and speaker labels. Does not transcribe the words again.",
                "Проверяет звук и подписи говорящих. Не распознаёт слова заново.");
            check.Click += async (_, _) => { if (_session is not null) { check.IsEnabled = false; await _services.RecheckSpeakers(_session.Directory); } };
            VoicesHost.Children.Add(check);
        }
        foreach (CallPerson person in people)
        {
            VoicesHost.Children.Add(BuildVoiceCard(person, people, busy));
        }
    }
    private readonly HashSet<string> _renaming = [];
    private readonly HashSet<string> _expanded = [];
    private readonly Dictionary<string, int> _rejected = [];
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
    private void Resplit(int wanted)
    {
        if (_session is not null)
        {
            ResetPanelState();
            _ = _services.Resplit(_session.Directory, wanted);
        }
    }
    private void ResetPanelState()
    {
        _observedMixedLines.Clear();
        _renaming.Clear();
        _expanded.Clear();
        _rejected.Clear();
        _quoteButtons.Clear();
    }
    private void ShowNotThisMenu(FrameworkElement anchor, CallLine quote)
    {
        if (_transcript is null || _session is null)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        if (_transcript.Voices.Count == 0)
        {
            menu.Items.Add(SplitOrGetModelItem(_session.Directory));
        }
        else
        {
            AddSaidBy(menu, quote, reject: true);
        }

        menu.IsOpen = true;
    }
    private void AddSaidBy(ItemsControl menu, CallLine line, bool reject)
    {
        foreach (CallPerson person in People())
        {
            bool current = (line.Voice is { } voice && person.Voices.Contains(voice))
                || (person.IsMe && line.Channel == CallChannel.Mine && line.Voice is null);
            if (reject && current)
            {
                continue;
            }

            string header = person switch
            {
                { IsMe: true } => Settings.EffectiveMyName,
                { Name: { } name } => name,
                _ => $"{L.S.TranscriptVoice} {person.Voices[0]}",
            };

            var item = new MenuItem
            {
                Header = header,
                IsCheckable = !reject,
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

                CountRejection();

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

        menu.Items.Add(new Separator());

        // Новый голос — следующая свободная буква: реплика станет отдельной
        // карточкой «Кто это?».
        var someone = new MenuItem { Header = L.S.VoiceSomeoneElse };
        someone.Click += (_, _) =>
        {
            CountRejection();
            ReassignLine(line, CallVoices.NextFree(_transcript!.Voices));
        };
        menu.Items.Add(someone);

        void CountRejection()
        {
            if (reject && line.Voice is { } from)
            {
                _rejected[from] = _rejected.GetValueOrDefault(from) + 1;
            }
        }
    }
    private static Ellipse MenuDot(Brush color)
    {
        Ellipse dot = Ui.Dot(color, Tokens.Dot);
        dot.Margin = new Thickness(0);
        return dot;
    }
    private MenuItem SplitOrGetModelItem(string directory)
    {
        if (_services.CanSplitVoices())
        {
            var split = new MenuItem { Header = L.S.VoiceSomeoneElseSplit };
            split.Click += (_, _) => Resplit(2);
            return split;
        }

        var get = new MenuItem { Header = L.S.VoiceSomeoneElseGetModel };
        IReadOnlyList<Tapybara.Core.Models.ModelKind> missing = ModelNeeds.Missing(Settings, ModelNeeds.SplitVoices);
        get.IsEnabled = missing.Count > 0;
        get.Click += (_, _) => _services.FetchModel(missing[0]);
        return get;
    }
    private void NamePerson(CallPerson person, string? name, IReadOnlyList<CallPerson> people)
    {
        if (person.IsMe)
        {
            return;
        }

        // «павел» и «Павел» — один человек; пишем так, как он уже назван.
        if (name is not null && people.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is { Name: { } same })
        {
            name = same;
        }

        if (IsWholeSide(person))
        {
            SetOtherSideName(name);
            return;
        }

        SetNames(person.Voices, name);
    }
    private void SetNames(IReadOnlyList<string> voices, string? name)
    {
        if (_session is null)
        {
            return;
        }

        string directory = _session.Directory;
        _session = CallMeta.Update(directory, s =>
        {
            var names = new Dictionary<string, string>(s.VoiceNames);
            foreach (string voice in voices)
            {
                if (name is null)
                {
                    names.Remove(voice);
                }
                else
                {
                    names[voice] = name;
                }
            }

            return s with { VoiceNames = names };
        }) ?? _session;

        if (name != CallSession.Me)
        {
            RememberName(name);
            foreach (string voice in voices)
            {
                LearnVoice(voice, name);
            }
        }

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
