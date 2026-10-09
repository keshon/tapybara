using System.Windows;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Wpf.Ui.Controls;

namespace Tapybara.App;

/// <summary>Общий транспорт и независимые состояния редактора, позиции и ограниченного прослушивания.</summary>
public partial class CallsPage
{
    private static readonly TimeSpan QuotePadding = TimeSpan.FromMilliseconds(300);
    private CallLine? _playingQuote;
    private bool _playbackFailed;
    private string? _playbackSourceKey;

    private bool PlayingSelectedCall => _current is not null
        && string.Equals(_player.CallDirectory, _current.Directory, StringComparison.OrdinalIgnoreCase);

    private void InitializeTransport()
    {
        Transport.ToggleRequested += TogglePlayback;
        Transport.StopRequested += () => _player.Stop();
        Transport.SeekRequested += SeekPlayback;
        Transport.RewindRequested += () => SeekPlayback(_player.Position - TimeSpan.FromSeconds(5));
        Transport.ForwardRequested += () => SeekPlayback(_player.Position + TimeSpan.FromSeconds(5));
        Transport.WholeCallRequested += () => SeekPlayback(_player.Position);
        Transport.FollowRequested += () => FollowPlayback(force: true);
        LineList.PreviewMouseWheel += (_, _) => Controls.SmoothScroll.Cancel(LineList);
    }

    private void ConfigurePlayback(CallEntry? entry)
    {
        string? sourceKey = entry is null ? null : WaveformKey(entry);
        if (entry is not null && sourceKey == _playbackSourceKey
            && string.Equals(_player.CallDirectory, entry.Directory, StringComparison.OrdinalIgnoreCase))
        {
            UpdateTransport();
            return;
        }
        _playbackSourceKey = sourceKey;
        _playbackTimer.Stop();
        Controls.SmoothScroll.Cancel(LineList);
        Transport.ResetInteraction();
        Transport.Loop = false;
        _playingQuote = null;
        _playbackFailed = false;
        if (entry is null || entry.State is CallState.Recording or CallState.Damaged)
        {
            _player.Clear();
        }
        else
        {
            _playbackFailed = !_player.Load(entry.Directory);
        }
        UpdateTransport();
    }

    private void TogglePlayback()
    {
        if (_current is null) { return; }
        if (PlayingSelectedCall && _player.IsPlaying)
        {
            _player.Pause();
            _playbackTimer.Stop();
        }
        else
        {
            if (Timeline.Range is { } range && (!_player.HasRange || _player.RangeStart != range.Start || _player.RangeEnd != range.End
                || _player.Channel != (range.Source is CallChannel source ? source : null)))
            {
                Timeline.PlayRange();
                return;
            }
            if (!PlayingSelectedCall || !_player.IsLoaded) { _player.Load(_current.Directory); }
            _playbackFailed = !_player.Resume();
            if (!_playbackFailed) { _playbackTimer.Start(); }
            else { _playbackTimer.Stop(); }
        }
        FollowPlayback();
        UpdateTransport();
    }

    private void OnTimestampShift(Controls.TranscriptLineActions anchor, TimeSpan shift)
    {
        if (anchor.DataContext is not TranscriptLineRow row) { return; }
        TimeSpan from = row.Line.Start + shift;
        TimeSpan to = row.Line.End + shift;
        if (from < TimeSpan.Zero) { to -= from; from = TimeSpan.Zero; }
        OpenBoundaryEditor(anchor, row.Line, add: false, proposed: (from, to));
    }

    private void OnStampClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TranscriptLineRow row }) { SeekPlayback(row.Line.Start); }
    }

    private void OnFragmentClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TranscriptLineRow row }) { PlayQuote(row.Line); }
    }

    private void SeekPlayback(TimeSpan from)
    {
        if (_current is null) { return; }
        if (!PlayingSelectedCall || !_player.IsLoaded) { _player.Load(_current.Directory); }
        _playingQuote = null;
        Timeline.ClearRange();
        _playbackFailed = !_player.Seek(from, wholeCall: true);
        if (_player.IsPlaying) { _playbackTimer.Start(); }
        else { _playbackTimer.Stop(); }
        FollowPlayback();
        UpdateQuoteButtons();
        UpdateTransport();
    }

    /// <summary>Ограниченный фрагмент физического источника; назначение другого человека не меняет канал.</summary>
    private void PlayQuote(CallLine quote)
    {
        if (_current is null) { return; }
        if (_playingQuote?.IsSameAs(quote) == true && PlayingSelectedCall && _player.IsPlaying)
        {
            _player.Stop();
            return;
        }
        TimeSpan from = quote.Start - QuotePadding;
        Timeline.ClearRange();
        _playbackFailed = !_player.Play(_current.Directory, from < TimeSpan.Zero ? TimeSpan.Zero : from,
            quote.End + QuotePadding, quote.Channel);
        _playingQuote = _playbackFailed ? null : quote;
        if (!_playbackFailed) { _playbackTimer.Start(); FollowPlayback(); }
        else { _playbackTimer.Stop(); }
        UpdateTransport();
        UpdateQuoteButtons();
    }

    /// <summary>Отмечаем все пересекающиеся интервалы; в тишине ни один не остаётся активным.</summary>
    private void FollowPlayback(bool force = false)
    {
        if (!PlayingSelectedCall) { return; }
        TimeSpan position = _player.Position;
        TranscriptLineRow? before = _lines.FirstOrDefault(l => l.IsCurrent);
        TranscriptLineRow? first = null;
        foreach (TranscriptLineRow row in _lines)
        {
            bool active = CallPlaybackActivity.IsActive(row.Line, position, _player.Channel);
            row.IsCurrent = active;
            row.IsPlaying = active && _player.IsPlaying;
            row.IsExcerptPlaying = _player.IsPlaying && _playingQuote?.IsSameAs(row.Line) == true;
            if (active) { first ??= row; }
        }
        if (Transport.Follow && !Transport.IsScrubbing && !LineList.IsMouseCaptureWithin && first is not null && (first != before || force)
            && _activeEditor?.IsKeyboardFocusWithin != true && _activeEditor?.HasDraftChanges != true
            && _activeEditor?.ContextMenu?.IsOpen != true)
        {
            Controls.SmoothScroll.Reveal(LineList, first);
        }
        Timeline.SelectInterval(first?.Line.Id);
        UpdateTransport();
    }

    private void OnPlayerStopped()
    {
        if (_player.CompletedNaturally && !_player.LastPlaybackFailed && Transport.Loop
            && PlayingSelectedCall && _player.HasRange)
        {
            _playbackFailed = !_player.Resume();
            if (!_playbackFailed) { _playbackTimer.Start(); FollowPlayback(force: true); UpdateTransport(); return; }
        }
        _playbackFailed = _player.LastPlaybackFailed;
        _playbackTimer.Stop();
        foreach (TranscriptLineRow row in _lines)
        {
            row.IsCurrent = false;
            row.IsPlaying = false;
            row.IsExcerptPlaying = false;
        }
        UpdateTransport();
        UpdateQuoteButtons();
    }

    private void UpdateTransport()
    {
        bool selected = PlayingSelectedCall;
        Transport.SetState(selected ? _player.Position : TimeSpan.Zero,
            selected ? _player.Duration : TimeSpan.Zero, selected && _player.IsPlaying, selected && _player.IsLoaded,
            selected && Timeline.Range is { } range ? (range.Start, range.End)
                : selected && _player.HasRange ? (_player.RangeStart, _player.RangeEnd) : null,
            _playbackFailed ? L.S.PlaybackFailed : null);
        Timeline.SetPosition(selected ? _player.Position : TimeSpan.Zero);
    }

}
