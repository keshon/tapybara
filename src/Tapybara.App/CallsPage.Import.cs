using System.Windows;
using Tapybara.Core.Calls;
using Tapybara.App.Localization;

namespace Tapybara.App;

public partial class CallsPage
{
    private CancellationTokenSource? _importCancellation;
    private sealed record SpeakerCountChoice(int Count, string Label);
    private sealed record ImportLanguageChoice(string Code, string Label);

    private void UpdateImportButton()
    {
        NewTranscriptionButton.Content = null;
        NewTranscriptionButton.Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Add24 };
        NewTranscriptionButton.ToolTip = SpeakerText("New transcription", "Новая транскрипция");
        System.Windows.Automation.AutomationProperties.SetName(NewTranscriptionButton, NewTranscriptionButton.ToolTip.ToString());
        NewTranscriptionButton.IsEnabled = _services.ImportMedia is not null;
        Controls.ControlSizing.Apply(NewTranscriptionButton, iconOnly: true);
        ImportTitle.Text = SpeakerText("New transcription", "Новая транскрипция");
        ImportDropTitle.Text = SpeakerText("Drop audio or video here", "Перетащите сюда аудио или видео");
        ImportDropHint.Text = SpeakerText("or click to browse your files", "или нажмите, чтобы выбрать файл");
        CloseImportButton.ToolTip = SpeakerText("Back to transcript", "Вернуться к транскрипту");
        System.Windows.Automation.AutomationProperties.SetName(CloseImportButton, CloseImportButton.ToolTip.ToString());
        System.Windows.Automation.AutomationProperties.SetName(BrowseImportButton, ImportDropTitle.Text + ". " + ImportDropHint.Text);
        ImportLanguageLabel.Text = L.S.FieldRecognitionLanguage;
        System.Windows.Automation.AutomationProperties.SetName(ImportLanguage, ImportLanguageLabel.Text);
        string selected = ImportLanguage.SelectedValue as string ?? Settings.Language;
        ImportLanguage.ItemsSource = WhisperLanguages.All.Select(l => new ImportLanguageChoice(l.Code,
            l.Code == WhisperLanguages.AutoCode ? L.S.LanguageAutoDetect : l.ToString())).ToList();
        ImportLanguage.SelectedValue = selected;
        if (ImportLanguage.SelectedIndex < 0) ImportLanguage.SelectedValue = WhisperLanguages.AutoCode;
        Controls.ControlSizing.Apply(ImportLanguage);
        ImportSpeakersLabel.Text = SpeakerText("Speakers (optional)", "Говорящие (необязательно)");
        int chosenCount = ImportSpeakers.SelectedValue as int? ?? 0;
        ImportSpeakers.ItemsSource = new[] { new SpeakerCountChoice(0, SpeakerText("Automatic", "Автоматически")) }
            .Concat(Enumerable.Range(1, 16).Select(n => new SpeakerCountChoice(n, n.ToString(L.S.Formatting)))).ToList();
        ImportSpeakers.SelectedValue = chosenCount;
        Controls.ControlSizing.Apply(ImportSpeakers);
        ImportSpeakers.ToolTip = SpeakerText("Count everyone heard in the file, including yourself. Names are assigned after transcription.",
            "Считайте всех, кого слышно в файле, включая себя. Имена назначаются после распознавания.");
        System.Windows.Automation.AutomationProperties.SetName(ImportSpeakers, ImportSpeakersLabel.Text);
        Controls.ControlSizing.Apply(CancelImportButton);
        CancelImportButton.Content = L.S.ButtonCancel;
        bool importing = _importCancellation is not null;
        BrowseImportButton.IsEnabled = !importing;
        ImportLanguage.IsEnabled = !importing;
        ImportSpeakers.IsEnabled = !importing && _services.CanSeparateSpeakers();
        ImportProgress.Visibility = importing ? Visibility.Visible : Visibility.Collapsed;
        CancelImportButton.Visibility = importing ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnNewTranscription(object sender, RoutedEventArgs e)
    {
        if (_services.ImportMedia is null || _activeEditor?.Flush() == false) return;
        if (ImportPane.Visibility != Visibility.Visible && _importCancellation is null)
        {
            ImportLanguage.SelectedValue = Settings.Language;
            ImportSpeakers.SelectedValue = 0;
            ImportStatusText.Text = string.Empty;
        }
        SaveNote();
        SaveTitle();
        ImportPane.Visibility = Visibility.Visible;
        UpdateImportButton();
        BrowseImportButton.Focus();
    }

    private void OnCloseImport(object sender, RoutedEventArgs e)
    {
        _importCancellation?.Cancel();
        ImportPane.Visibility = Visibility.Collapsed;
        NewTranscriptionButton.Focus();
    }

    private void OnCancelImport(object sender, RoutedEventArgs e) => _importCancellation?.Cancel();

    private async void OnBrowseImport(object sender, RoutedEventArgs e)
    {
        if (_importCancellation is not null) return;
        var picker = new Microsoft.Win32.OpenFileDialog {
            Title = SpeakerText("New transcription", "Новая транскрипция"),
            Filter = SpeakerText("Audio and video", "Аудио и видео") + "|*.wav;*.mp3;*.m4a;*.mp4;*.mkv;*.webm;*.ogg;*.flac;*.aac;*.wma;*.wmv;*.mov;*.avi|" +
                SpeakerText("All files", "Все файлы") + "|*.*", Multiselect = false };
        if (picker.ShowDialog(Window.GetWindow(this)) == true) await ImportFileAsync(picker.FileName);
    }

    private void OnImportDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = _importCancellation is null && TryImportFile(e.Data) is not null ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnImportDrop(object sender, System.Windows.DragEventArgs e)
    {
        e.Handled = true;
        if (_importCancellation is not null) return;
        if (TryImportFile(e.Data) is { } path) await ImportFileAsync(path);
        else ImportStatusText.Text = SpeakerText("Choose one audio or video file.", "Выберите один аудио- или видеофайл.");
    }

    private static string? TryImportFile(System.Windows.IDataObject data) =>
        data.GetData(System.Windows.DataFormats.FileDrop) is string[] { Length: 1 } files && System.IO.File.Exists(files[0]) ? files[0] : null;

    private async Task ImportFileAsync(string path)
    {
        if (_importCancellation is not null || _services.ImportMedia is null) return;
        string language = ImportLanguage.SelectedValue as string ?? Settings.Language;
        int? speakerCount = ImportSpeakers.SelectedValue is int n && n > 0 ? n : null;
        using var cancellation = new CancellationTokenSource();
        _importCancellation = cancellation;
        ImportStatusText.Text = SpeakerText("Importing · ", "Импорт · ") + System.IO.Path.GetFileName(path);
        UpdateImportButton();
        try
        {
            CallSession session = await _services.ImportMedia(path, language, speakerCount, cancellation.Token);
            if (_disposed || cancellation.IsCancellationRequested) return;
            ImportPane.Visibility = Visibility.Collapsed;
            Select(session.Directory);
            if (CallLibrary.Describe(session.Directory) is { } entry) await TranscribeCallAsync(entry);
        }
        catch (OperationCanceledException)
        {
            if (!_disposed) ImportStatusText.Text = SpeakerText("Import cancelled. Choose another file to start again.", "Импорт отменён. Выберите файл, чтобы начать заново.");
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            if (!_disposed)
                ImportStatusText.Text = ex is System.ComponentModel.Win32Exception
                    ? SpeakerText("Windows could not decode this format. For additional formats, install FFmpeg on PATH or place ffmpeg.exe beside Tapybara.exe.",
                        "Windows не поддерживает этот формат. Для дополнительных форматов добавьте FFmpeg в PATH или положите ffmpeg.exe рядом с Tapybara.exe.") : ex.Message;
        }
        finally
        {
            _importCancellation = null;
            if (!_disposed) UpdateImportButton();
        }
    }
}
