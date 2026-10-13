using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Tapybara.App.Controls;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Tapybara.Core.Diagnostics;
using Tapybara.Core.Export;
using Tapybara.Core.Windows;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace Tapybara.App;

/// <summary>Единая доставка: одна внешняя копия для буфера и файла.</summary>
public partial class CallsPage
{
    private Popup? _exportPopup;

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_current is not null)
        {
            OpenExport(_current);
        }
    }

    private void OpenExport(CallEntry entry)
    {
        if (_activeEditor?.Flush() == false)
        {
            return;
        }
        CallSession session = CallMeta.Load(entry.Directory) ?? entry.Session;
        CallTranscript? document = CallTranscriptStore.Load(entry.Directory);
        List<TranscriptExportSelection>? selection = null;
        if (_session?.Directory.Equals(entry.Directory, StringComparison.OrdinalIgnoreCase) == true
            && _activeEditor is { SelectionLength: > 0, DataContext: TranscriptLineRow row } editor)
        {
            selection = [new TranscriptExportSelection(row.Line.Id, editor.SelectionStart, editor.SelectionLength)];
        }
        IReadOnlyList<string> names = document is null ? [] : CallExport.AliasNames(session, document,
            Settings.KnownParticipants, Settings.EffectiveMyName, OtherSideLabel(Settings), L.S.TranscriptVoice);
        var panel = new TranscriptExportPanel(names, Settings.PersonAliases, selection is not null, document is not null);
        if (_exportPopup is not null)
        {
            _exportPopup.IsOpen = false;
        }
        var preview = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 160, Margin = new Thickness(0, Tokens.Space2, 0, 0) };
        AutomationProperties.SetName(preview, L.S.ExportPreview);
        var copy = new Wpf.Ui.Controls.Button { Content = L.S.ExportCopy, Appearance = Wpf.Ui.Controls.ControlAppearance.Primary };
        var save = new Wpf.Ui.Controls.Button { Content = L.S.ExportSave, Margin = new Thickness(Tokens.Space2, 0, 0, 0) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, Tokens.Space3, 0, 0) };
        footer.Children.Add(copy);
        footer.Children.Add(save);
        var content = new StackPanel();
        content.Children.Add(Ui.BodyStrong(TitleOf(session)));
        content.Children.Add(panel);
        content.Children.Add(Ui.Caption(L.S.ExportPreview));
        content.Children.Add(preview);
        var layout = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = content, MaxHeight = Math.Min(500, SystemParameters.WorkArea.Height * 0.65),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var card = new PopoverSurface { Child = layout, Width = 520, MaxWidth = SystemParameters.WorkArea.Width - Tokens.Space5 * 2 };
        card.Padding = new Thickness(Tokens.Space3);
        var popup = new Popup { Child = card, PlacementTarget = ExportButton.IsVisible ? ExportButton : CallList,
            Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        _exportPopup = popup;
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_exportPopup, popup)) { _exportPopup = null; }
        };
        TranscriptExportOptions Options() => panel.Options with { Selection = panel.SelectionOnly ? selection : null };
        string Render(CallSession currentSession, CallTranscript? currentDocument, TranscriptExportOptions options) =>
            currentDocument is null ? TranscriptText(currentSession.TranscriptPath)
                : CallExport.Render(currentSession, currentDocument, options, Settings.EffectiveMyName, OtherSideLabel(Settings), L.S.TranscriptVoice);
        void UpdatePreview()
        {
            try
            {
                TranscriptExportOptions options = Options();
                // Превью ограничено первыми репликами; доставка ниже всегда читает полную свежую версию.
                TranscriptExportOptions sampleOptions = document is null || options.Selection is not null ? options
                    : options with { Selection = document.Lines.Take(5)
                        .Select(line => new TranscriptExportSelection(line.Id, 0, line.Text.Length)).ToArray() };
                string text = Render(session, document, sampleOptions);
                preview.Text = text.Length > 4000 ? text[..4000] + "…" : text;
                copy.IsEnabled = save.IsEnabled = true;
            }
            catch (ArgumentException)
            {
                preview.Text = L.S.ExportInvalid;
                copy.IsEnabled = save.IsEnabled = false;
            }
            catch (InvalidOperationException)
            {
                preview.Text = L.S.ExportInvalid;
                copy.IsEnabled = save.IsEnabled = false;
            }
        }
        void Deliver(bool clipboard)
        {
            try
            {
                TranscriptExportOptions options = Options();
                CallSession latestSession = CallMeta.Load(entry.Directory) ?? session;
                CallTranscript? latest = CallTranscriptStore.Load(entry.Directory);
                if (document is not null && latest is null)
                {
                    throw new InvalidOperationException(L.S.ExportInvalid);
                }
                string output = Render(latestSession, latest, options);
                if (clipboard)
                {
                    ClipboardWriter.SetText(output, excludeFromHistory: false);
                    popup.IsOpen = false;
                    return;
                }
                string extension = options.Format switch { TranscriptExportFormat.Text => "txt", TranscriptExportFormat.Markdown => "md", _ => "json" };
                string filename = string.Concat(TitleOf(latestSession).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                var dialog = new Microsoft.Win32.SaveFileDialog { FileName = filename + "." + extension,
                    DefaultExt = "." + extension, Filter = extension.ToUpperInvariant() + "|*." + extension };
                popup.IsOpen = false;
                if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                {
                    CallExport.ValidateDestination(latestSession, dialog.FileName);
                    File.WriteAllText(dialog.FileName, output, new System.Text.UTF8Encoding(false));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
            {
                AppLog.Warn("Не удалось экспортировать транскрипт.", ex);
                preview.Text = string.Format(L.S.Formatting, L.S.ExportFailed, ex.Message);
                if (!popup.IsOpen)
                {
                    ConfirmWindow.Ask(Window.GetWindow(this), new ConfirmWindow(L.S.CallsExport, preview.Text, L.S.EditApply, L.S.ButtonCancel));
                }
            }
        }
        panel.OptionsChanged += (_, _) => UpdatePreview();
        copy.Click += (_, _) => Deliver(clipboard: true);
        save.Click += (_, _) => Deliver(clipboard: false);
        UpdatePreview();
        AnchoredPopup.Open(popup, ExportButton.IsVisible ? ExportButton : CallList);
        panel.Focus();
    }
}
