using System.Windows;
using System.Windows.Controls;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Tapybara.Core.Speech;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Tapybara.App;

public partial class CallsPage
{
    private Window? _speakerCorrectionWindow;
    private static string SpeakerText(string en, string ru) => L.S.Formatting.TwoLetterISOLanguageName == "ru" ? ru : en;

    private void ShowSpeakerHint(string text)
    {
        BannerText.Text = text;
        BannerButton.Visibility = Visibility.Collapsed;
        Banner.Visibility = Visibility.Visible;
    }

    private Wpf.Ui.Controls.FluentWindow OpenSpeakerReview(StackPanel body)
    {
        _speakerCorrectionWindow?.Close();
        var window = new Wpf.Ui.Controls.FluentWindow { Title = SpeakerText("Review speaker assignments", "Проверка подписей говорящих"),
            Owner = Window.GetWindow(this), Width = 520, Height = 650, MinWidth = 360, MinHeight = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            FontFamily = FontFamily, FontSize = Tokens.Body };
        window.SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        window.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        _speakerCorrectionWindow = window;
        window.Closed += (_, _) =>
        {
            if (_speakerCorrectionWindow == window) _speakerCorrectionWindow = null;
            if (_playingQuote is not null) _player.Stop();
        };
        return window;
    }

    private void ReviewSpeakerAssignments(string? personName = null)
    {
        if (_activeEditor?.Flush() == false || _session is null || _transcript is null) return;
        string directory = _session.Directory;
        CallSession call = _session;
        int voiceCount = _transcript.Voices.Count;
        var examples = SpeakerCorrection.Examples(_transcript);
        var body = new StackPanel { Margin = new Thickness(20) };
        var window = OpenSpeakerReview(body);
        body.Children.Add(Ui.BodyStrong(SpeakerText("Listen and choose who is speaking", "Послушайте и выберите говорящего")));
        body.Children.Add(Ui.Caption(SpeakerText("Uncertain phrases come first. Use one clear voice as an example.",
            "Сомнительные реплики показаны первыми. Выберите чёткую речь одного человека.")));
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
        body.Children.Add(status);
        if (examples.Count == 0)
        {
            status.Text = SpeakerText("No suitable examples: matching needs at least four seconds without overlapping phrases. You can still correct short phrases from the transcript speaker menu.",
                "Нет подходящих образцов: нужны четыре секунды без наложения реплик. Короткие реплики можно исправить через меню говорящего в транскрипте.");
            var done = new Button { Content = SpeakerText("Back to transcript", "К транскрипту") };
            done.Click += (_, _) => { window.Close(); LineList.Focus(); };
            body.Children.Add(done);
            window.Show();
            return;
        }
        int index = 0;
        var choosePhrase = new ComboBox { ItemsSource = examples.Select(line =>
            CallTranscriptRenderer.Stamp(line.Start) + " · " + (line.Text.Length > 48 ? line.Text[..48] + "…" : line.Text)).ToList(),
            SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 12) };
        Controls.ControlSizing.Apply(choosePhrase);
        body.Children.Add(choosePhrase);
        var phrase = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        var original = new TextBlock { TextWrapping = TextWrapping.Wrap };
        body.Children.Add(original);
        body.Children.Add(phrase);
        var navigation = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var previous = new Button { Content = SpeakerText("Previous", "Назад"), Margin = new Thickness(0, 0, 8, 0) };
        var listen = new Button { Content = SpeakerText("Listen", "Послушать"), Margin = new Thickness(0, 0, 8, 0) };
        var next = new Button { Content = SpeakerText("Next phrase", "Другая реплика") };
        navigation.Children.Add(previous); navigation.Children.Add(listen); navigation.Children.Add(next);
        body.Children.Add(navigation);
        body.Children.Add(new TextBlock { Text = SpeakerText("Who is speaking?", "Кто говорит?"), Margin = new Thickness(0, 20, 0, 6) });
        var names = People().Where(p => !p.IsMe && p.Name is not null).Select(p => p.Name!)
            .Concat(_session.Participants).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var who = new ComboBox { ItemsSource = names, SelectedItem = personName };
        Controls.ControlSizing.Apply(who);
        body.Children.Add(who);
        body.Children.Add(new TextBlock { Text = SpeakerText("Or enter another name", "Или введите другое имя"), Margin = new Thickness(0, 12, 0, 6) });
        var newName = new TextBox();
        Controls.ControlSizing.Apply(newName);
        body.Children.Add(newName);
        var confirm = new Button { Content = SpeakerText("Confirm and find similar speech", "Подтвердить и найти похожие реплики"),
            Margin = new Thickness(0, 16, 0, 8), IsEnabled = !string.IsNullOrWhiteSpace(personName) };
        body.Children.Add(confirm);
        body.Children.Add(Ui.Caption(SpeakerText("Confirms this phrase only. You will review other matches before applying them.",
            "Подтверждается только эта реплика. Остальные совпадения проверяются перед применением.")));
        var close = new Button { Content = SpeakerText("Cancel", "Отмена"), Margin = new Thickness(0, 12, 0, 0) };
        close.Click += (_, _) => window.Close();
        body.Children.Add(close);
        void Display()
        {
            var line = examples[index];
            status.Text = SpeakerText($"Phrase {index + 1} of {examples.Count} · {CallTranscriptRenderer.Stamp(line.Start)}",
                $"Реплика {index + 1} из {examples.Count} · {CallTranscriptRenderer.Stamp(line.Start)}");
            original.Text = SpeakerText("Currently assigned to: ", "Сейчас подписано: ") + CallSpeakers.Label(line,
                call, voiceCount, Settings.EffectiveMyName, OtherSideLabel(Settings), L.S.TranscriptVoice);
            phrase.Text = line.Text;
            if (choosePhrase.SelectedIndex != index) choosePhrase.SelectedIndex = index;
            previous.IsEnabled = index > 0;
            next.IsEnabled = index < examples.Count - 1;
        }
        previous.Click += (_, _) => { _player.Stop(); index--; Display(); };
        next.Click += (_, _) => { _player.Stop(); index++; Display(); };
        choosePhrase.SelectionChanged += (_, _) =>
        {
            if (choosePhrase.SelectedIndex >= 0 && choosePhrase.SelectedIndex != index)
            {
                _player.Stop(); index = choosePhrase.SelectedIndex; Display();
            }
        };
        listen.Click += (_, _) =>
        {
            if (_session?.Directory == directory) PlayQuote(examples[index]);
            else status.Text = SpeakerText("Return to the original call, or cancel and start again.", "Вернитесь к исходному звонку или отмените проверку.");
        };
        void UpdateConfirm() => confirm.IsEnabled = !string.IsNullOrWhiteSpace(newName.Text)
            || who.SelectedItem is string;
        who.SelectionChanged += (_, _) => UpdateConfirm();
        newName.TextChanged += (_, _) => UpdateConfirm();
        confirm.Click += (_, _) =>
        {
            if (_activeEditor?.Flush() == false) return;
            if (_session?.Directory != directory || _transcript is null)
            {
                status.Text = SpeakerText("Return to the original call, or cancel and start again.", "Вернитесь к исходному звонку или отмените проверку.");
                return;
            }
            string? name = !string.IsNullOrWhiteSpace(newName.Text) ? newName.Text.Trim() : who.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(name)) return;
            string? existing = People().FirstOrDefault(p => !p.IsMe && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?
                .Voices.FirstOrDefault(v => v != CallVoices.WholeOtherSide);
            string voice = existing ?? CallVoices.NextFree(_transcript.Voices);
            var example = examples[index];
            try
            {
                if (!ExecuteEdit(directory, t => SpeakerCorrection.Confirm(t, example, voice), metadataChange: s => s with
                    { VoiceNames = new Dictionary<string, string>(s.VoiceNames) { [voice] = name } }))
                {
                    status.Text = SpeakerText("Could not save. Close this review and try again.", "Не удалось сохранить. Закройте проверку и попробуйте снова.");
                    return;
                }
                var seed = _transcript!.Lines.Single(l => l.Id == example.Id);
                window.Close();
                FindSimilarSpeech(seed);
            }
            catch (InvalidOperationException)
            {
                confirm.IsEnabled = false;
                status.Text = SpeakerText("This phrase changed. Cancel and restart the review.", "Реплика изменилась. Отмените и начните проверку заново.");
            }
        };
        Display();
        window.Show();
    }

    private async void FindSimilarSpeech(CallLine seed)
    {
        if (_activeEditor?.Flush() == false || _session is null || _transcript is null
            || _services.FindSpeakerSpeech is null) return;
        CallSession call = _session;
        string directory = call.Directory;
        CallTranscript snapshot = _transcript;
        if (seed.Voice is null || seed.Channel != CallChannel.Theirs || seed.End - seed.Start < VoiceCheck.MinLine)
        {
            ShowSpeakerHint(SpeakerText("Choose a phrase of at least four seconds, assigned to the correct person.",
                "Выберите реплику от четырёх секунд, назначенную нужному человеку."));
            return;
        }
        string name = CallSpeakers.Label(seed, _session, snapshot.Voices.Count, Settings.EffectiveMyName,
            OtherSideLabel(Settings), L.S.TranscriptVoice);
        var body = new StackPanel { Margin = new Thickness(20) };
        var status = new TextBlock { Text = SpeakerText("Finding similar speech…", "Ищем похожие реплики…"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        body.Children.Add(Ui.BodyStrong(SpeakerText("Find speech by ", "Найти реплики: ") + name));
        body.Children.Add(Ui.Caption(SpeakerText("Example: ", "Образец: ") + CallTranscriptRenderer.Stamp(seed.Start)));
        var sample = new Button { Content = SpeakerText("Listen to example", "Послушать образец"), Margin = new Thickness(0, 8, 0, 12) };
        sample.Click += (_, _) => { if (_session?.Directory == directory) PlayQuote(seed); };
        body.Children.Add(sample);
        body.Children.Add(status);
        var results = new StackPanel();
        body.Children.Add(results);
        var apply = new Button { Content = SpeakerText("Reassign selected", "Назначить выбранные"), IsEnabled = false,
            Margin = new Thickness(0, 16, 0, 0) };
        body.Children.Add(apply);
        body.Children.Add(Ui.Caption(SpeakerText("Changes speaker labels only. Undo restores the whole batch.",
            "Меняются только подписи. Отмена вернёт всю группу реплик.")));
        var otherExample = new Button { Content = SpeakerText("Choose another example", "Выбрать другой образец"), Margin = new Thickness(0, 12, 0, 0) };
        body.Children.Add(otherExample);
        var close = new Button { Content = SpeakerText("Cancel", "Отмена"), Margin = new Thickness(0, 8, 0, 0) };
        body.Children.Add(close);
        var window = OpenSpeakerReview(body);
        otherExample.Click += (_, _) => { window.Close(); ReviewSpeakerAssignments(name); };
        close.Click += (_, _) => window.Close();
        var cancel = new CancellationTokenSource();
        window.Closed += (_, _) => { cancel.Cancel(); cancel.Dispose(); };
        window.Show();
        var choices = new List<(CheckBox Box, CallLine Line)>();
        try
        {
            var candidates = await _services.FindSpeakerSpeech(directory, snapshot, seed, cancel.Token);
            if (cancel.IsCancellationRequested) return;
            status.Text = candidates.Count == 0
                ? SpeakerText("No reliable matches. Try a longer, clearer example.", "Надёжных совпадений нет. Попробуйте более длинный и чёткий образец.")
                : SpeakerText($"{candidates.Count} suggestions. Listen before applying. Short and overlapping phrases are skipped.",
                    $"Предложений: {candidates.Count}. Послушайте перед применением. Короткие реплики и наложения пропущены.");
            foreach (var candidate in candidates)
            {
                var row = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
                var box = new CheckBox { Content = CallTranscriptRenderer.Stamp(candidate.Line.Start), IsChecked = candidate.Clear };
                row.Children.Add(box);
                string from = CallSpeakers.Label(candidate.Line, call, snapshot.Voices.Count,
                    Settings.EffectiveMyName, OtherSideLabel(Settings), L.S.TranscriptVoice);
                row.Children.Add(Ui.Caption(from + " → " + name));
                row.Children.Add(new TextBlock { Text = candidate.Line.Text, TextWrapping = TextWrapping.Wrap });
                row.Children.Add(Ui.Caption(candidate.Clear
                    ? SpeakerText("Closer to confirmed examples", "Ближе к подтверждённым образцам")
                    : SpeakerText("Needs listening · no clear competing example", "Нужно послушать · нет явного отличия от других образцов")));
                var listen = new Button { Content = SpeakerText("Listen", "Послушать"), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
                listen.Click += (_, _) => { if (_session?.Directory == directory) PlayQuote(candidate.Line); };
                row.Children.Add(listen);
                results.Children.Add(row);
                choices.Add((box, candidate.Line));
                box.Checked += (_, _) => UpdateApply();
                box.Unchecked += (_, _) => UpdateApply();
            }
            UpdateApply();
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException)
        {
            if (!cancel.IsCancellationRequested) status.Text = SpeakerText(
                "This example may contain multiple voices or too little speech. Choose a clear single-speaker example of at least four seconds.",
                "В образце могут быть разные голоса или слишком мало речи. Выберите чёткую речь одного человека от четырёх секунд.");
        }
        catch (Exception ex)
        {
            Tapybara.Core.Diagnostics.AppLog.Warn("Speaker correction analysis failed.", ex);
            if (!cancel.IsCancellationRequested) status.Text = SpeakerText("Could not analyse voices. Check the voice embedding model in Settings.",
                "Не удалось сравнить голоса. Проверьте модель слепков голоса в настройках.");
            if (!cancel.IsCancellationRequested)
            {
                var models = new Button { Content = SpeakerText("Open voice model settings", "Открыть настройки модели голоса") };
                models.Click += (_, _) => { window.Close(); _services.FetchModel(Tapybara.Core.Models.ModelKind.VoiceEmbedding); };
                body.Children.Add(models);
            }
        }
        void UpdateApply()
        {
            int count = choices.Count(c => c.Box.IsChecked == true);
            apply.IsEnabled = count > 0;
            apply.Content = SpeakerText($"Reassign {count} phrases", $"Назначить реплики: {count}");
        }
        apply.Click += (_, _) =>
        {
            if (_activeEditor?.Flush() == false) return;
            if (_session?.Directory != directory)
            {
                status.Text = SpeakerText("Return to the original call to apply these changes.",
                    "Вернитесь к исходному звонку, чтобы применить изменения.");
                return;
            }
            var selected = choices.Where(c => c.Box.IsChecked == true).Select(c => c.Line).ToList();
            try
            {
                if (ExecuteEdit(directory, t => SpeakerCorrection.Apply(t, seed, selected)))
                {
                    apply.IsEnabled = false;
                    results.IsEnabled = false;
                    status.Text = SpeakerText($"{selected.Count} phrases reassigned to {name}.", $"Назначено реплик: {selected.Count}. Говорящий: {name}.");
                    apply.Content = SpeakerText("Applied", "Применено");
                    close.Content = SpeakerText("Done", "Готово");
                    var appliedLines = _transcript!.Lines.ToArray();
                    var appliedNames = CallMeta.Load(directory)?.VoiceNames;
                    var undo = new Button { Content = SpeakerText("Undo this batch", "Отменить эту группу"), Margin = new Thickness(0, 8, 0, 0) };
                    undo.Click += (_, _) =>
                    {
                        if (_activeEditor?.Flush() == false) return;
                        if (_session?.Directory == directory && _editSessions.TryGetValue(directory, out var edits)
                            && edits.CanUndo && edits.Document.Lines.SequenceEqual(appliedLines)
                            && appliedNames is not null && CallMeta.Load(directory) is { } meta
                            && meta.VoiceNames.Count == appliedNames.Count
                            && appliedNames.All(pair => meta.VoiceNames.GetValueOrDefault(pair.Key) == pair.Value))
                        {
                            _noteHistoryActive = false;
                            RestoreHistory(redo: false);
                            if (!edits.Document.Lines.SequenceEqual(appliedLines)) window.Close();
                            else status.Text = SpeakerText("Could not undo. Close this review and check transcript history.",
                                "Не удалось отменить. Закройте проверку и проверьте историю транскрипта.");
                        }
                        else status.Text = SpeakerText("Later edits exist. Use transcript Undo to undo them in order.",
                            "Есть более поздние правки. Отменяйте их по порядку через «Отменить» в транскрипте.");
                    };
                    body.Children.Add(undo);
                    _ = _services.RefreshPrints(directory);
                }
                else status.Text = SpeakerText("Could not save. Choose another example to refresh the review.",
                    "Не удалось сохранить. Выберите другой образец для повторной проверки.");
            }
            catch (InvalidOperationException)
            {
                apply.IsEnabled = false;
                status.Text = SpeakerText("The transcript changed. Close this review and find matches again.",
                    "Транскрипт изменился. Закройте проверку и найдите совпадения заново.");
            }
        };
    }
}
