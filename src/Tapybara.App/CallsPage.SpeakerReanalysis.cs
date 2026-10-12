using System.Windows;
using System.Windows.Controls;
using Tapybara.Core.Calls;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace Tapybara.App;

public partial class CallsPage
{
    private async void AnalyseSpeakerGroups(IReadOnlyList<string>? focusedVoices = null)
    {
        if (_activeEditor?.Flush() == false || _session is null || _transcript is null) return;
        var call = _session;
        var snapshot = _transcript;
        if (focusedVoices is not null)
        {
            snapshot = SpeakerReanalysis.Scope(snapshot, focusedVoices);
        }
        string directory = call.Directory;
        var body = new StackPanel { Margin = new Thickness(20) };
        var window = OpenSpeakerReview(body);
        body.Children.Add(Ui.BodyStrong(focusedVoices is null
            ? SpeakerText("Re-analyse speaker assignments", "Проверить голоса заново")
            : SpeakerText("Separate this mixed voice", "Разделить этот смешанный голос")));
        body.Children.Add(Ui.Caption(SpeakerText("The app groups similar speech. Listen to examples, then name the groups you recognise.",
            "Приложение группирует похожие голоса. Послушайте образцы и подпишите группы, которые узнаёте.")));
        var status = new TextBlock { Text = SpeakerText("Analysing audio…", "Анализируем звук…"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
        body.Children.Add(status);
        var showAll = new CheckBox { Content = SpeakerText("Show all groups", "Показать все группы"),
            IsChecked = false, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 12) };
        body.Children.Add(showAll);
        var groupsHost = new StackPanel();
        body.Children.Add(groupsHost);
        var apply = new Button { Content = SpeakerText("Apply named groups", "Применить подписанные группы"),
            IsEnabled = false, Margin = new Thickness(0, 12, 0, 0) };
        body.Children.Add(apply);
        var cancelButton = new Button { Content = SpeakerText("Cancel", "Отмена"), Margin = new Thickness(0, 8, 0, 0) };
        body.Children.Add(cancelButton);
        var cancel = new CancellationTokenSource();
        window.Closed += (_, _) => { cancel.Cancel(); cancel.Dispose(); };
        cancelButton.Click += (_, _) => window.Close();
        window.Show();
        var choices = new List<(SpeakerReanalysis.Group Group, CheckBox Include, ComboBox Name,
            List<(CheckBox Box, CallLine Line)> Lines, FrameworkElement Card, bool NeedsAttention)>();
        var names = People().Where(p => !p.IsMe && p.Name is not null).Select(p => p.Name!)
            .Concat(call.Participants).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        bool applied = false;
        bool analysing = true;
        void UpdateApply()
        {
            if (applied) return;
            int count = choices.Where(c => c.Card.Visibility == Visibility.Visible && c.Include.IsChecked == true && !string.IsNullOrWhiteSpace(c.Name.Text))
                .Sum(c => c.Lines.Count(l => l.Box.IsChecked == true));
            apply.IsEnabled = count > 0;
            apply.Content = SpeakerText($"Apply assignments for {count} phrases", $"Назначить реплики: {count}");
        }
        try
        {
            if (_services.AnalyseSpeakers is null) throw new InvalidOperationException("Speaker analysis is unavailable.");
            var progress = new Progress<int>(percent =>
            {
                if (analysing && !cancel.IsCancellationRequested) status.Text = SpeakerText($"Analysing voices… {percent}%", $"Анализируем голоса… {percent}%");
            });
            var report = await _services.AnalyseSpeakers(directory, snapshot, progress, cancel.Token);
            if (cancel.IsCancellationRequested) return;
            analysing = false;
            status.Text = report.Groups.Count == 0
                ? SpeakerText("No suitable unconfirmed speech. Short or overlapping phrases need individual review.",
                    "Нет подходящих неподтверждённых реплик. Короткие реплики и наложения требуют отдельной проверки.")
                : SpeakerText($"{report.Groups.Count} proposed groups · {report.Skipped} phrases skipped. Groups may contain mistakes; compare the examples. Unnamed groups stay unchanged.",
                    $"Предложено групп: {report.Groups.Count} · пропущено реплик: {report.Skipped}. Сравните образцы: в группах возможны ошибки. Неподписанные группы не изменятся.");
            int number = 0;
            foreach (var group in report.Groups)
            {
                number++;
                var card = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
                var include = new CheckBox { IsChecked = true,
                    Content = SpeakerText($"Group {number} · {group.Lines.Count} phrases", $"Группа {number} · реплик: {group.Lines.Count}") };
                card.Children.Add(include);
                card.Children.Add(Ui.Caption(group.SuggestedName is { } suggested
                    ? SpeakerText("Suggested: ", "Предположение: ") + suggested
                    : SpeakerText("Unidentified voice", "Неопознанный голос")));
                var sourceNames = group.Lines.Select(l => CallSpeakers.Label(l, call, snapshot.Voices.Count,
                    Settings.EffectiveMyName, OtherSideLabel(Settings), Tapybara.App.Localization.L.S.TranscriptVoice)).Distinct();
                card.Children.Add(Ui.Caption(SpeakerText("Currently: ", "Сейчас: ") + string.Join(", ", sourceNames)));
                foreach (var example in group.Examples)
                {
                    var listen = new Button { Content = SpeakerText("Listen · ", "Послушать · ") + CallTranscriptRenderer.Stamp(example.Start),
                        Margin = new Thickness(0, 6, 0, 4), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
                    listen.Click += (_, _) =>
                    {
                        if (_session?.Directory == directory) PlayQuote(example);
                        else status.Text = SpeakerText("Return to the original call to listen.", "Вернитесь к исходному звонку для прослушивания.");
                    };
                    card.Children.Add(listen);
                    card.Children.Add(new TextBlock { Text = example.Text, TextWrapping = TextWrapping.Wrap });
                }
                card.Children.Add(new TextBlock { Text = SpeakerText("Assign this group to", "Кому назначить группу"), Margin = new Thickness(0, 12, 0, 4) });
                var name = new ComboBox { IsEditable = true, ItemsSource = names, IsTextSearchEnabled = true };
                Controls.ControlSizing.Apply(name);
                card.Children.Add(name);
                card.Children.Add(Ui.Caption(SpeakerText("Choose a person or type a new name", "Выберите человека или введите новое имя")));
                if (group.SuggestedName is { } suggestion)
                {
                    var accept = new Button { Content = SpeakerText("Use ", "Подтвердить: ") + suggestion, Margin = new Thickness(0, 6, 0, 0) };
                    accept.Click += (_, _) => { name.Text = suggestion; UpdateApply(); };
                    card.Children.Add(accept);
                }
                var allLines = new StackPanel();
                var lineChoices = new List<(CheckBox Box, CallLine Line)>();
                foreach (var line in group.Lines)
                {
                    var box = new CheckBox { IsChecked = true, Content = new TextBlock { Text = CallTranscriptRenderer.Stamp(line.Start) + " · " + line.Text,
                        TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(0, 6, 0, 4) };
                    var play = new Button { Content = SpeakerText("Listen", "Послушать") };
                    play.Click += (_, _) => { if (_session?.Directory == directory) PlayQuote(line); };
                    allLines.Children.Add(box); allLines.Children.Add(play);
                    lineChoices.Add((box, line));
                    box.Checked += (_, _) => UpdateApply(); box.Unchecked += (_, _) => UpdateApply();
                }
                card.Children.Add(new Expander { Header = SpeakerText("Review or exclude individual phrases", "Проверить или исключить отдельные реплики"),
                    Content = allLines, Margin = new Thickness(0, 8, 0, 0) });
                var surface = Ui.Card(card);
                groupsHost.Children.Add(surface);
                choices.Add((group, include, name, lineChoices, surface,
                    SpeakerReanalysis.NeedsAttention(group, voice => CallSpeakers.NameOf(call, voice))));
                include.Checked += (_, _) => UpdateApply(); include.Unchecked += (_, _) => UpdateApply();
                name.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdateApply()));
                name.SelectionChanged += (_, _) => UpdateApply();
            }
            void FilterGroups()
            {
                if (applied) return;
                int visible = 0;
                foreach (var choice in choices)
                {
                    bool show = showAll.IsChecked == true || choice.NeedsAttention;
                    choice.Card.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    if (show)
                    {
                        visible++;
                        choice.Include.Content = SpeakerText($"Group {visible} · {choice.Group.Lines.Count} phrases",
                            $"Группа {visible} · реплик: {choice.Group.Lines.Count}");
                    }
                }
                int hidden = choices.Count - visible;
                status.Text = choices.Count == 0
                    ? SpeakerText("No suitable unconfirmed speech. Short or overlapping phrases need individual review.",
                        "Нет подходящих неподтверждённых реплик. Короткие реплики и наложения требуют отдельной проверки.")
                    : visible == 0
                        ? SpeakerText($"No proposed corrections in this view. {hidden} groups agree with their current labels. Show all to inspect them.",
                            $"В этом режиме нет предложенных исправлений. Групп с совпадающими подписями: {hidden}. Их можно открыть через «Показать все группы».")
                        : SpeakerText($"{visible} groups to review · {hidden} matching groups hidden · {report.Skipped} phrases skipped. Unnamed and hidden groups stay unchanged.",
                            $"Групп для проверки: {visible} · скрыто с совпадающими подписями: {hidden} · пропущено реплик: {report.Skipped}. Неподписанные и скрытые группы не изменятся.");
                UpdateApply();
            }
            showAll.Visibility = choices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            showAll.Checked += (_, _) => FilterGroups();
            showAll.Unchecked += (_, _) => FilterGroups();
            FilterGroups();
            var retry = new Button { Content = SpeakerText("Analyse again", "Проанализировать снова"), Margin = new Thickness(0, 12, 0, 0) };
            retry.Click += (_, _) => { window.Close(); AnalyseSpeakerGroups(focusedVoices); };
            body.Children.Add(retry);
            var manual = new Button { Content = SpeakerText("Use a specific example instead", "Использовать конкретный образец"), Margin = new Thickness(0, 8, 0, 0) };
            manual.Click += (_, _) => { window.Close(); ReviewSpeakerAssignments(); };
            body.Children.Add(manual);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            analysing = false;
            Tapybara.Core.Diagnostics.AppLog.Warn("Speaker re-analysis failed.", ex);
            if (!cancel.IsCancellationRequested)
            {
                status.Text = SpeakerText("Analysis failed. Check the voice model, then retry. No labels were changed.",
                    "Анализ не удался. Проверьте модель голоса и повторите. Подписи не изменены.");
                var models = new Button { Content = SpeakerText("Voice model settings", "Настройки модели голоса") };
                models.Click += (_, _) => _services.FetchModel(Tapybara.Core.Models.ModelKind.VoiceEmbedding);
                body.Children.Add(models);
                var retry = new Button { Content = SpeakerText("Retry", "Повторить") };
                retry.Click += (_, _) => { window.Close(); AnalyseSpeakerGroups(focusedVoices); };
                body.Children.Add(retry);
            }
        }
        apply.Click += (_, _) =>
        {
            if (applied || _activeEditor?.Flush() == false) return;
            if (_session?.Directory != directory || _transcript is null)
            {
                status.Text = SpeakerText("Return to the original call to apply these assignments.", "Вернитесь к исходному звонку для применения."); return;
            }
            var voiceNames = new Dictionary<string, string>(_session.VoiceNames);
            var voices = _transcript.Voices.ToList();
            var assignments = new List<SpeakerReanalysis.Assignment>();
            foreach (var choice in choices.Where(c => c.Card.Visibility == Visibility.Visible && c.Include.IsChecked == true && !string.IsNullOrWhiteSpace(c.Name.Text)))
            {
                var lines = choice.Lines.Where(l => l.Box.IsChecked == true).Select(l => l.Line).ToList();
                if (lines.Count == 0) continue;
                string name = choice.Name.Text.Trim();
                string? voice = voiceNames.FirstOrDefault(p => string.Equals(p.Value, name, StringComparison.OrdinalIgnoreCase)
                    && p.Key != CallVoices.WholeOtherSide).Key;
                if (voice is null)
                {
                    voice = CallVoices.NextFree(voices); voices.Add(voice); voiceNames[voice] = name;
                }
                assignments.Add(new(lines, voice));
            }
            if (assignments.Count == 0) return;
            var before = _transcript.Lines.ToArray();
            try
            {
                if (!ExecuteEdit(directory, t => SpeakerReanalysis.Apply(t, assignments), metadataChange: s => s with { VoiceNames = voiceNames }))
                { status.Text = SpeakerText("The transcript changed. Analyse again before applying.", "Транскрипт изменился. Повторите анализ."); return; }
                applied = true; apply.IsEnabled = false; groupsHost.IsEnabled = false;
                showAll.IsEnabled = false;
                status.Text = SpeakerText($"Updated {assignments.Sum(a => a.Lines.Count)} phrases. Confirmed assignments were preserved.",
                    $"Обновлено реплик: {assignments.Sum(a => a.Lines.Count)}. Подтверждённые подписи сохранены.");
                cancelButton.Content = SpeakerText("Done", "Готово");
                var after = _transcript!.Lines.ToArray();
                var undo = new Button { Content = SpeakerText("Undo this batch", "Отменить эту группу изменений"), Margin = new Thickness(0, 8, 0, 0) };
                undo.Click += (_, _) =>
                {
                    if (_activeEditor?.Flush() == false) return;
                    if (_session?.Directory == directory && _editSessions.TryGetValue(directory, out var edits)
                        && edits.CanUndo && edits.Document.Lines.SequenceEqual(after)
                        && CallMeta.Load(directory) is { } meta && meta.VoiceNames.Count == voiceNames.Count
                        && voiceNames.All(p => meta.VoiceNames.GetValueOrDefault(p.Key) == p.Value))
                    {
                        _noteHistoryActive = false; RestoreHistory(redo: false);
                        if (edits.Document.Lines.SequenceEqual(before)) window.Close();
                        else status.Text = SpeakerText("Could not undo. Check transcript history.", "Не удалось отменить. Проверьте историю транскрипта.");
                    }
                    else status.Text = SpeakerText("Later edits exist. Undo them in order from transcript history.", "Есть более поздние правки. Отмените их по порядку через историю транскрипта.");
                };
                body.Children.Add(undo);
                _ = _services.RefreshPrints(directory);
            }
            catch (InvalidOperationException)
            { apply.IsEnabled = false; status.Text = SpeakerText("The phrases changed. Analyse again before applying.", "Реплики изменились. Повторите анализ перед применением."); }
        };
    }
}
