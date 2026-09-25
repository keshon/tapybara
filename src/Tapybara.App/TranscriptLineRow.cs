using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;

using Brush = System.Windows.Media.Brush;

namespace Tapybara.App;

/// <summary>Одна реплика в транскрипте окна.</summary>
public sealed class TranscriptLineRow : INotifyPropertyChanged
{
    private bool _isCurrent;
    private bool _isPlaying;
    private double _progress;

    public required CallLine Line { get; init; }

    public required string Stamp { get; init; }

    public required string Speaker { get; init; }

    public required Brush SpeakerBrush { get; init; }

    /// <summary>Неназванный голос — курсивом: это подпись машины, а не имя.</summary>
    public required System.Windows.FontStyle SpeakerFontStyle { get; init; }

    /// <summary>Имя показывается только на смене говорящего, как в transcript.md.</summary>
    public required Visibility SpeakerVisibility { get; init; }

    /// <summary>Цвет имени: основной текст, у неназванного голоса — вторичный.</summary>
    public required Brush SpeakerForeground { get; init; }

    /// <summary>Воздух перед сменой говорящего: реплики одного человека идут плотнее.</summary>
    public Thickness Spacing => SpeakerVisibility == Visibility.Visible
        ? new Thickness(0, Tokens.Space2, 0, 0)
        : new Thickness(0);

    public required string PlayHint { get; init; }

    /// <summary>Голос реплики под сомнением (см. <see cref="CallLine.IsDoubtful"/>).</summary>
    public Visibility DoubtVisibility => Line.IsDoubtful ? Visibility.Visible : Visibility.Collapsed;

    public string DoubtHint { get; } = L.S.LineDoubtful;

    public string Text => Line.Text;

    /// <summary>Эту реплику сейчас слышно.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent != value)
            {
                _isCurrent = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
            }
        }
    }

    /// <summary>Эту реплику слышно прямо сейчас: у времени ■ вместо ▶.</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying != value)
            {
                _isPlaying = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
            }
        }
    }

    /// <summary>Сколько реплики уже прозвучало, 0–1: столько строки и залито.</summary>
    public double Progress
    {
        get => _progress;
        set
        {
            if (Math.Abs(_progress - value) > 0.001)
            {
                _progress = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
