using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Tapybara.App.Localization;

namespace Tapybara.App.Controls;

/// <summary>Общие поля интервала аудио с валидацией; воспроизведение и сохранение остаются у экрана.</summary>
public sealed class AudioBoundaryEditor : StackPanel
{
    private readonly Wpf.Ui.Controls.TextBox _from;
    private readonly Wpf.Ui.Controls.TextBox _to;
    private readonly Wpf.Ui.Controls.TextBox? _text;
    public event Action<TimeSpan, TimeSpan, string?>? ApplyRequested;
    public event Action<TimeSpan, TimeSpan>? PlayRequested;

    private static System.Windows.Controls.TextBlock Caption(string text)
    {
        var label = new System.Windows.Controls.TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        label.SetResourceReference(StyleProperty, "TextBodySecondary");
        return label;
    }

    public AudioBoundaryEditor(TimeSpan from, TimeSpan to, TimeSpan maximum, string? text = null,
        Func<TimeSpan, TimeSpan, bool>? validRange = null, string? applyLabel = null, string? help = null)
    {
        Width = 360;
        var description = Caption(help ?? L.S.EditBoundaryHelp);
        description.Margin = new Thickness(0, 0, 0, Tokens.Space3);
        Children.Add(description);
        var startLabel = Caption(L.S.EditFrom);
        startLabel.Margin = new Thickness(0, 0, 0, Tokens.Space1);
        Children.Add(startLabel);
        _from = new Wpf.Ui.Controls.TextBox { Text = AudioTime.Format(from), ClearButtonEnabled = false };
        ControlSizing.Apply(_from);
        AutomationProperties.SetName(_from, L.S.EditFrom);
        Children.Add(_from);
        var endLabel = Caption(L.S.EditTo);
        endLabel.Margin = new Thickness(0, Tokens.Space3, 0, Tokens.Space1);
        Children.Add(endLabel);
        _to = new Wpf.Ui.Controls.TextBox { Text = AudioTime.Format(to), ClearButtonEnabled = false };
        ControlSizing.Apply(_to);
        AutomationProperties.SetName(_to, L.S.EditTo);
        Children.Add(_to);
        if (text is not null)
        {
            _text = new Wpf.Ui.Controls.TextBox { Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetName(_text, L.S.LineEditText);
            Children.Add(_text);
        }
        var play = new Wpf.Ui.Controls.Button { Content = L.S.CallsListen, Margin = new Thickness(0, Tokens.Space3, Tokens.Space2, 0) };
        var apply = new Wpf.Ui.Controls.Button { Content = applyLabel ?? L.S.EditApply, Margin = new Thickness(0, Tokens.Space3, 0, 0) };
        ControlSizing.Apply(play);
        ControlSizing.Apply(apply);
        Children.Add(new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Children = { play, apply } });

        bool Read(out TimeSpan begin, out TimeSpan end)
        {
            begin = default;
            end = default;
            if (!AudioTime.TryParse(_from.Text, out begin) || !AudioTime.TryParse(_to.Text, out end)
                || end <= begin || end.TotalSeconds > maximum.TotalSeconds + 0.0005)
            {
                return false;
            }
            end = end >= maximum ? maximum : end;
            if (begin >= end) { return false; }
            return validRange?.Invoke(begin, end) != false;
        }
        void Refresh()
        {
            play.IsEnabled = Read(out _, out _);
            apply.IsEnabled = play.IsEnabled && (_text is null || !string.IsNullOrWhiteSpace(_text.Text));
        }
        _from.TextChanged += (_, _) => Refresh();
        _to.TextChanged += (_, _) => Refresh();
        if (_text is not null)
        {
            _text.TextChanged += (_, _) => Refresh();
        }
        play.Click += (_, _) =>
        {
            if (Read(out TimeSpan begin, out TimeSpan end))
            {
                PlayRequested?.Invoke(begin, end);
            }
        };
        apply.Click += (_, _) =>
        {
            if (Read(out TimeSpan begin, out TimeSpan end))
            {
                ApplyRequested?.Invoke(begin, end, _text?.Text);
            }
        };
        Refresh();
    }
}
