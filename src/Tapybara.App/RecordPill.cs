using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBlock = System.Windows.Controls.TextBlock;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Tapybara.App;

/// <summary>
/// Кнопка записи — та же пилюля, что плавает поверх окон во время записи.
/// </summary>
/// <remarks>
/// <para>
/// Раньше это была иконка ● в рамке, неотличимая от кнопки «папка» рядом.
/// А запись — главное действие страницы и единственное место, где в
/// приложении есть красный. Поэтому кнопка выглядит как индикатор, который
/// человек видит на экране во время записи: тёмная пилюля, красная точка —
/// у звонка точка в кольце, — надпись и горячая клавиша. Нажал — и то же
/// самое появляется поверх всех окон.
/// </para>
/// <para>
/// Пилюля тёмная в обеих темах, как и индикатор: это цитата из него, а не
/// обычная кнопка, которой положено следовать теме.
/// </para>
/// </remarks>
public sealed class RecordPill : Button
{
    private static readonly Brush Red = Frozen(0xFF, 0x4D, 0x4D);

    private readonly Ellipse _dot;
    private readonly TextBlock _label;
    private readonly TextBlock _hint;
    private bool _pulsing;

    /// <param name="call">Звонок: точка в кольце, как на пилюле звонка.</param>
    public RecordPill(bool call)
    {
        Style = Ui.StyleOf("RecordPillButton");

        var mark = new Grid { Width = 13, Height = 13, VerticalAlignment = VerticalAlignment.Center };
        if (call)
        {
            mark.Children.Add(new Ellipse { Stroke = Red, StrokeThickness = 2 });
        }

        _dot = new Ellipse
        {
            Width = call ? 5 : 9,
            Height = call ? 5 : 9,
            Fill = Red,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        mark.Children.Add(_dot);

        _label = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Tokens.Space2 + 2, 0, 0, 0),
        };

        _hint = new TextBlock
        {
            FontSize = Tokens.Caption,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Tokens.Space3, 0, 0, 0),
        };

        // Надпись слева, подсказка справа: в широкой кнопке подсказка уходит
        // к правому краю, в узкой колонка-распорка просто схлопывается.
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_label, 1);
        Grid.SetColumn(_hint, 3);
        content.Children.Add(mark);
        content.Children.Add(_label);
        content.Children.Add(_hint);
        Content = content;
    }

    /// <summary>Ничего не пишется: название действия и его горячая клавиша.</summary>
    public void ShowIdle(string label, string hotkey)
    {
        IsEnabled = true;
        _label.Text = label;
        _hint.Text = hotkey;
        _hint.Visibility = string.IsNullOrWhiteSpace(hotkey) ? Visibility.Collapsed : Visibility.Visible;
        Pulse(false);
    }

    /// <summary>Идёт запись: «Стоп» и сколько уже пишется, точка дышит.</summary>
    public void ShowRecording(string label, TimeSpan elapsed)
    {
        IsEnabled = true;
        _label.Text = label;
        _hint.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
        _hint.Visibility = Visibility.Visible;
        Pulse(true);
    }

    /// <summary>Записано, идёт распознавание: нажимать нечего.</summary>
    public void ShowBusy(string label)
    {
        IsEnabled = false;
        _label.Text = label;
        _hint.Visibility = Visibility.Collapsed;
        Pulse(false);
    }

    private void Pulse(bool on)
    {
        if (on == _pulsing)
        {
            return;
        }

        _pulsing = on;
        _dot.BeginAnimation(OpacityProperty, on
            ? new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(700))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            }
            : null);
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
