using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Tapybara.App.Localization;
using Tapybara.Core.Export;

using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;

namespace Tapybara.App.Controls;

/// <summary>Переиспользуемые параметры внешней копии текста; доставка и чтение документа принадлежат экрану.</summary>
public sealed class TranscriptExportPanel : StackPanel
{
    private sealed record LabelChoice(string Label, TranscriptSpeakerLabels Value);
    private readonly ComboBox _format = new() { ItemsSource = Enum.GetValues<TranscriptExportFormat>(), SelectedIndex = 0 };
    private readonly ComboBox _labels = new() { DisplayMemberPath = nameof(LabelChoice.Label), SelectedIndex = 0 };
    private readonly CheckBox _timestamps = new() { Content = L.S.ExportTimestamps, IsChecked = true };
    private readonly CheckBox _selection;
    private readonly CheckBox _mentions = new() { Content = L.S.ExportMentions, IsChecked = true };
    private readonly StackPanel _aliases = new();
    private readonly Dictionary<string, Wpf.Ui.Controls.TextBox> _fields = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? OptionsChanged;

    public bool SelectionOnly => _selection.IsChecked == true;
    public TranscriptExportOptions Options => new()
    {
        Format = (TranscriptExportFormat)_format.SelectedItem,
        Labels = ((LabelChoice)_labels.SelectedItem).Value,
        Timestamps = _timestamps.IsChecked == true,
        ReplaceMentions = _mentions.IsChecked == true,
        Aliases = _fields.ToDictionary(p => p.Key, p => p.Value.Text.Trim(), StringComparer.OrdinalIgnoreCase),
    };

    public TranscriptExportPanel(IReadOnlyList<string> names, IReadOnlyDictionary<string, string> remembered, bool selectionAvailable,
        bool structured = true)
    {
        Width = 420;
        _selection = new CheckBox { Content = L.S.ExportSelection, IsEnabled = selectionAvailable };
        _labels.ItemsSource = new[]
        {
            new LabelChoice(L.S.ExportNames, TranscriptSpeakerLabels.Names),
            new LabelChoice(L.S.ExportAliases, TranscriptSpeakerLabels.Aliases),
            new LabelChoice(L.S.ExportNoLabels, TranscriptSpeakerLabels.None),
        };
        _labels.SelectedIndex = 0;
        AddCaption(L.S.ExportFormat);
        Children.Add(_format);
        AddCaption(L.S.ExportNames);
        Children.Add(_labels);
        Children.Add(_timestamps);
        Children.Add(_selection);
        Children.Add(_mentions);
        Children.Add(_aliases);
        if (!structured)
        {
            _format.ItemsSource = new[] { TranscriptExportFormat.Markdown };
            _format.SelectedIndex = 0;
            _labels.IsEnabled = false;
            _timestamps.IsEnabled = false;
        }
        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            string fallback = string.Format(L.S.Formatting, L.S.AnonymizeParticipant, i + 1);
            string alias = remembered.TryGetValue(name, out string? known) && !known.Equals(name, StringComparison.OrdinalIgnoreCase) ? known : fallback;
            var field = new Wpf.Ui.Controls.TextBox { Text = alias, Margin = new Thickness(0, 0, 0, Tokens.Space2) };
            AutomationProperties.SetName(field, L.S.ExportAliases + ": " + name);
            field.TextChanged += (_, _) => OptionsChanged?.Invoke(this, EventArgs.Empty);
            _fields[name] = field;
            var caption = new TextBlock { Text = name };
            caption.SetResourceReference(StyleProperty, "TextCaption");
            _aliases.Children.Add(caption);
            _aliases.Children.Add(field);
        }
        void UpdateVisibility()
        {
            bool aliases = (_labels.SelectedItem as LabelChoice)?.Value == TranscriptSpeakerLabels.Aliases;
            _aliases.Visibility = aliases ? Visibility.Visible : Visibility.Collapsed;
            _mentions.Visibility = aliases ? Visibility.Visible : Visibility.Collapsed;
        }
        _labels.SelectionChanged += (_, _) => { UpdateVisibility(); OptionsChanged?.Invoke(this, EventArgs.Empty); };
        _format.SelectionChanged += (_, _) => OptionsChanged?.Invoke(this, EventArgs.Empty);
        foreach (CheckBox option in new[] { _timestamps, _selection, _mentions })
        {
            option.Click += (_, _) => OptionsChanged?.Invoke(this, EventArgs.Empty);
            option.Margin = new Thickness(0, Tokens.Space2, 0, 0);
        }
        _format.Margin = new Thickness(0, 0, 0, Tokens.Space2);
        _labels.Margin = new Thickness(0, 0, 0, Tokens.Space2);
        AutomationProperties.SetName(_format, L.S.CallsExport);
        AutomationProperties.SetName(_labels, L.S.ExportNames);
        UpdateVisibility();
    }
    private void AddCaption(string text)
    {
        var caption = new TextBlock { Text = text };
        caption.SetResourceReference(StyleProperty, "TextCaption");
        Children.Add(caption);
    }

}
