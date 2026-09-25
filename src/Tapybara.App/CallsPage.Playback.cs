using System.Windows;
using System.Windows.Input;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Wpf.Ui.Controls;

namespace Tapybara.App;

/// <summary>Прослушивание: звонок с реплики, цитаты голосов и звучащая строка.</summary>
public partial class CallsPage
{
    /// <summary>
    /// Запас вокруг цитаты при прослушивании.
    /// </summary>
    /// <remarks>
    /// Границы реплики Whisper ставит по словам, и первый слог иногда
    /// попадает на долю секунды раньше. Без запаса цитата начиналась бы
    /// с середины слова — а узнают человека как раз по началу фразы.
    /// </remarks>
    private static readonly TimeSpan QuotePadding = TimeSpan.FromMilliseconds(300);

    /// <summary>Какая реплика играет как цитата — её кнопка ▶ превращается в ■.</summary>
    private CallLine? _playingQuote;

    private void OnListenClick(object sender, RoutedEventArgs e)
    {
        if (_player.IsPlaying)
        {
            _player.Stop();
            return;
        }

        TimeSpan from = _lines.FirstOrDefault(l => l.IsCurrent)?.Line.Start ?? TimeSpan.Zero;
        PlayFrom(from);
    }

    private void OnStampClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TranscriptLineRow row })
        {
            return;
        }

        if (row.IsPlaying)
        {
            _player.Stop();
            return;
        }

        PlayFrom(row.Line.Start);
    }

    /// <summary>
    /// Щелчок по реплике — отсюда «Слушать» и начнёт.
    /// </summary>
    /// <remarks>
    /// Полоса звучащей реплики — это и место, откуда продолжится
    /// прослушивание. Раньше поставить её можно было только проиграв запись
    /// до нужного места. Пока звук идёт, щелчок ничего не переносит: человек
    /// ставит курсор, чтобы выделить или поправить слово, а не перескочить.
    /// </remarks>
    private void OnLinePressed(object sender, MouseButtonEventArgs e)
    {
        if (_player.IsPlaying || sender is not FrameworkElement { DataContext: TranscriptLineRow row })
        {
            return;
        }

        foreach (TranscriptLineRow line in _lines)
        {
            line.IsCurrent = line == row;
            line.Progress = 0;
        }
    }

    private void PlayFrom(TimeSpan from)
    {
        if (_current is null)
        {
            return;
        }

        _playingQuote = null;
        if (_player.Play(_current.Directory, from))
        {
            _playbackTimer.Start();
            FollowPlayback();
        }

        UpdateListenButton();
    }

    /// <summary>Послушать цитату голоса — только его дорожку, только эту реплику.</summary>
    private void PlayQuote(CallLine quote)
    {
        if (_current is null)
        {
            return;
        }

        if (_playingQuote == quote && _player.IsPlaying)
        {
            _player.Stop();
            return;
        }

        TimeSpan from = quote.Start - QuotePadding;
        bool started = _player.Play(
            _current.Directory,
            from < TimeSpan.Zero ? TimeSpan.Zero : from,
            quote.End + QuotePadding,
            CallChannel.Theirs);

        _playingQuote = started ? quote : null;
        if (started)
        {
            _playbackTimer.Start();
            FollowPlayback();
        }

        UpdateListenButton();
        UpdateQuoteButtons();
    }

    /// <summary>
    /// Подсветить реплику, которую сейчас слышно, и вести за ней список.
    /// </summary>
    /// <remarks>
    /// Список идёт за звуком, только пока человек сам на него смотрит: если
    /// прежняя звучащая реплика видна. Прокрутил вверх перечитать — звук
    /// играет дальше, а список не выдёргивается из-под глаз.
    /// </remarks>
    private void FollowPlayback()
    {
        if (!_player.IsPlaying || _current is null
            || !string.Equals(_player.CallDirectory, _current.Directory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        TimeSpan position = _player.Position;
        TranscriptLineRow? now = null;
        foreach (TranscriptLineRow row in _lines)
        {
            if (row.Line.Start > position)
            {
                break;
            }

            // Играет одна дорожка — подсвечиваем только её реплики.
            if (_player.Channel is null || row.Line.Channel == _player.Channel)
            {
                now = row;
            }
        }

        TranscriptLineRow? before = _lines.FirstOrDefault(l => l.IsCurrent);
        bool sweep = Settings.ShowPlayingProgress;
        foreach (TranscriptLineRow row in _lines)
        {
            row.IsCurrent = row == now;
            row.IsPlaying = row == now && _playingQuote is null;
            row.Progress = row == now && sweep ? Fraction(row.Line, position) : 0;
        }

        if (now is not null && now != before && (before is null || IsOnScreen(before)))
        {
            LineList.ScrollIntoView(now);
        }
    }

    private static double Fraction(CallLine line, TimeSpan position)
    {
        double length = (line.End - line.Start).TotalSeconds;
        return length <= 0 ? 1 : Math.Clamp((position - line.Start).TotalSeconds / length, 0, 1);
    }

    /// <summary>Видна ли строка в списке целиком или частью.</summary>
    private bool IsOnScreen(TranscriptLineRow row)
    {
        if (LineList.ItemContainerGenerator.ContainerFromItem(row) is not FrameworkElement item || !item.IsVisible)
        {
            return false;
        }

        double top = item.TransformToAncestor(LineList).Transform(new System.Windows.Point(0, 0)).Y;
        return top + item.ActualHeight > 0 && top < LineList.ActualHeight;
    }

    private void OnPlayerStopped()
    {
        _playbackTimer.Stop();

        // Остановились — заливка уходит, полоса остаётся: с этого места
        // «Слушать» продолжит.
        foreach (TranscriptLineRow row in _lines)
        {
            row.Progress = 0;
            row.IsPlaying = false;
        }

        _playingQuote = null;
        UpdateListenButton();
        UpdateQuoteButtons();
    }

    private void UpdateListenButton()
    {
        bool playing = _player.IsPlaying && _playingQuote is null;
        ListenButton.Content = playing ? L.S.CallsStopListening : L.S.CallsListen;
        ListenButton.Icon = new SymbolIcon { Symbol = playing ? SymbolRegular.Stop24 : SymbolRegular.Play24 };
    }

    // --- действия ------------------------------------------------------------
}
