using System.Windows;
using System.Windows.Controls;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Tapybara.Core.Speech;
using CheckBox = System.Windows.Controls.CheckBox;
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

    private async void FindSimilarSpeech(CallLine seed)
    {
        if (_activeEditor?.Flush() == false || _session is null || _transcript is null
            || _services.FindSpeakerSpeech is null) return;
        _speakerCorrectionWindow?.Close();
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
        var window = new Window { Title = SpeakerText("Review speaker matches", "Проверка похожих реплик"),
            Owner = Window.GetWindow(this), Width = 520, Height = 650, MinWidth = 360, MinHeight = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new ScrollViewer { Content = body,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, FontFamily = FontFamily, FontSize = Tokens.Body };
        window.SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        window.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        var cancel = new CancellationTokenSource();
        _speakerCorrectionWindow = window;
        window.Closed += (_, _) => { cancel.Cancel(); cancel.Dispose(); _speakerCorrectionWindow = null; };
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
                "Choose a clear example of at least four seconds with no overlapping speech.",
                "Выберите чёткий образец от четырёх секунд без наложения голосов.");
        }
        catch (Exception ex)
        {
            Tapybara.Core.Diagnostics.AppLog.Warn("Speaker correction analysis failed.", ex);
            if (!cancel.IsCancellationRequested) status.Text = SpeakerText("Could not analyse voices. Check the voice embedding model in Settings.",
                "Не удалось сравнить голоса. Проверьте модель слепков голоса в настройках.");
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
                    window.Close();
                    _ = _services.RefreshPrints(directory);
                }
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
