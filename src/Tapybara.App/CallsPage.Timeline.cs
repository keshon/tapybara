using System.IO;
using System.Windows;
using Tapybara.App.Controls;
using Tapybara.App.Localization;
using Tapybara.Core.Audio;
using Tapybara.Core.Calls;
using Tapybara.Core.Diagnostics;

namespace Tapybara.App;

/// <summary>Экран связывает физические источники и текущие строки с универсальным отображением времени.</summary>
public partial class CallsPage
{
    private readonly WaveformCache _waveforms = new();
    private CancellationTokenSource? _waveformCancellation;
    private Task? _waveformTask;
    private int _waveformEpoch;
    private string? _timelineDirectory;
    private string? _waveformRequestKey;
    private string? _waveformLoadedKey;
    private WaveformEnvelope? _microphoneWave;
    private WaveformEnvelope? _systemWave;
    private bool _waveformLoading;
    private bool _microphoneFailed;
    private bool _systemFailed;

    private void InitializeTimeline()
    {
        Timeline.RangeChanged += UpdateTransport;
        Timeline.SeekRequested += SeekPlayback;
        Timeline.RangePlayRequested += range =>
        {
            if (_current is null || _activeEditor?.Flush() == false) { return; }
            CallChannel? channel = range.Source is CallChannel source ? source : null;
            _playingQuote = null;
            _playbackFailed = !_player.Play(_current.Directory, range.Start, range.End, channel);
            if (!_playbackFailed) { _playbackTimer.Start(); }
            else { _playbackTimer.Stop(); }
            FollowPlayback();
            UpdateTransport();
        };
        Timeline.TimingAdjustmentRequested += adjustment =>
        {
            if (adjustment.Key is not Guid id || _activeEditor?.Flush() == false) { return; }
            CallLine? line = _transcript?.Lines.FirstOrDefault(l => l.Id == id);
            if (line is null || line.Start != adjustment.OriginalStart || line.End != adjustment.OriginalEnd) { return; }
            OpenBoundaryEditor(Timeline.TimingAnchor, line, add: false, proposed: (adjustment.Start, adjustment.End));
        };
        Timeline.IntervalRequested += key =>
        {
            if (key is not Guid id || _activeEditor?.Flush() == false) { return; }
            TranscriptLineRow? row = _lines.FirstOrDefault(r => r.Line.Id == id);
            if (row is null) { return; }
            Controls.SmoothScroll.Reveal(LineList, row);
            SeekPlayback(row.Line.Start);
        };
    }

    private void OnTimelineToggle(object sender, RoutedEventArgs e)
    {
        Timeline.Visibility = TimelineToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (TimelineToggle.IsChecked == true) { UpdateTimeline(_current); }
        else { Timeline.ResetInteraction(); CancelTimelineLoading(); }
    }

    private void CancelTimelineLoading()
    {
        _waveformEpoch++;
        _waveformCancellation?.Cancel();
        _waveformCancellation = null;
        _waveformRequestKey = null;
        _waveformLoading = false;
    }

    private async Task SettleTimelineBeforeProcessing(string directory)
    {
        if (!string.Equals(directory, _timelineDirectory, StringComparison.OrdinalIgnoreCase)) { return; }
        Task? pending = _waveformTask;
        CancelTimelineLoading();
        if (pending is not null) { await pending; }
    }

    private static string WaveformKey(CallEntry entry) => string.Join('|', entry.Directory, entry.AudioBytes,
        File.GetLastWriteTimeUtc(entry.Session.MicPath).Ticks, File.GetLastWriteTimeUtc(entry.Session.SystemPath).Ticks);

    private void UpdateTimeline(CallEntry? entry)
    {
        if (!string.Equals(_timelineDirectory, entry?.Directory, StringComparison.OrdinalIgnoreCase))
        {
            CancelTimelineLoading();
            _timelineDirectory = entry?.Directory;
            _waveformLoadedKey = null;
            _microphoneWave = _systemWave = null;
            _microphoneFailed = _systemFailed = false;
            Timeline.SetPosition(TimeSpan.Zero);
        }
        if (entry is null)
        {
            Timeline.SetData(TimeSpan.Zero, [], [], false);
            return;
        }
        string key = WaveformKey(entry);
        if (_waveformLoadedKey is not null && key != _waveformLoadedKey)
        {
            _waveformLoadedKey = null;
            _microphoneWave = _systemWave = null;
            _microphoneFailed = _systemFailed = false;
        }
        if (entry.State is CallState.Recording or CallState.Transcribing)
        {
            CancelTimelineLoading();
        }
        else if (TimelineToggle.IsChecked == true && IsLoaded && !_disposed)
        {
            if (key != _waveformRequestKey && key != _waveformLoadedKey)
            {
                CancelTimelineLoading();
                _waveformLoading = true;
                _microphoneWave = _systemWave = null;
                _microphoneFailed = _systemFailed = false;
                var cancellation = new CancellationTokenSource();
                _waveformCancellation = cancellation;
                _waveformRequestKey = key;
                _waveformTask = LoadWaveformsAsync(entry, key, _waveformEpoch, cancellation);
            }
        }
        RenderTimeline();
    }

    private async Task<(WaveformEnvelope Envelope, bool Failed)> ReadWaveformAsync(string path, CancellationToken cancellationToken)
    {
        try { return (await _waveforms.ReadAsync(path, cancellationToken), false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException
            or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            AppLog.Warn($"Не удалось построить огибающую {path}.", ex);
            return (WaveformEnvelope.Empty, true);
        }
    }

    private async Task LoadWaveformsAsync(CallEntry entry, string key, int epoch, CancellationTokenSource cancellation)
    {
        try
        {
            var results = await Task.WhenAll(entry.Session.ImportedMedia ? Task.FromResult((WaveformEnvelope.Empty, false)) : ReadWaveformAsync(entry.Session.MicPath, cancellation.Token),
                ReadWaveformAsync(entry.Session.SystemPath, cancellation.Token));
            if (_disposed || cancellation.IsCancellationRequested || epoch != _waveformEpoch
                || !string.Equals(_timelineDirectory, entry.Directory, StringComparison.OrdinalIgnoreCase)) { return; }
            (_microphoneWave, _microphoneFailed) = results[0];
            (_systemWave, _systemFailed) = results[1];
            _waveformLoadedKey = key;
            _waveformLoading = false;
            RenderTimeline();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_waveformCancellation, cancellation))
            {
                _waveformCancellation = null;
                _waveformRequestKey = null;
                _waveformLoading = false;
            }
            cancellation.Dispose();
        }
    }

    private void RenderTimeline()
    {
        if (TimelineToggle.IsChecked != true || _current is null || _timelineDirectory != _current.Directory) { return; }
        TimeSpan audioDuration = new[] { _microphoneWave?.Duration ?? TimeSpan.Zero, _systemWave?.Duration ?? TimeSpan.Zero,
            PlayingSelectedCall ? _player.Duration : TimeSpan.Zero }.Max();
        TimeSpan duration = audioDuration > TimeSpan.Zero ? audioDuration
            : _session?.Duration > TimeSpan.Zero ? _session.Duration : _lines.Select(r => r.Line.End).DefaultIfEmpty(TimeSpan.Zero).Max();
        string? Status(WaveformEnvelope? envelope, bool failed) => envelope is { Peaks.Count: > 0 } ? null
            : _waveformLoading ? L.S.TimelineLoading : failed ? L.S.TimelineFailed : L.S.TimelineUnavailable;
        TimelineLane[] sources =
        [
            new(L.S.TimelineMicrophone, VoicePalette.Me, [], _microphoneWave, Status(_microphoneWave, _microphoneFailed), CallChannel.Mine),
            new(L.S.TimelineSystem, VoicePalette.For(0), [], _systemWave, Status(_systemWave, _systemFailed), CallChannel.Theirs),
        ];
        if (_session?.ImportedMedia == true)
        {
            sources = sources[1..];
        }

        List<TimelineLane> speakers = [.. _lines.GroupBy(row => row.Speaker, StringComparer.OrdinalIgnoreCase).Select(group =>
            new TimelineLane(group.Key, group.First().SpeakerBrush, [.. group.Select(row =>
            {
                string source = row.Line.Channel == CallChannel.Mine ? L.S.TimelineMicrophone : L.S.TimelineSystem;
                string hint = row.Stamp + "–" + CallTranscriptRenderer.Stamp(row.Line.End) + " · " + source + " · " + row.Text;
                if (row.Line.TimingEstimated) { hint += " · " + L.S.EditEstimated; }
                if (row.Line.IsDoubtful) { hint += " · " + L.S.LineDoubtful; }
                return new TimelineInterval(row.Line.Id, row.Line.Start, row.Line.End, hint, row.Line.TimingEstimated, row.Line.IsDoubtful);
            })] ))];
        bool available = _current.State is not (CallState.Recording or CallState.Damaged)
            && (audioDuration > TimeSpan.Zero);
        Timeline.SetData(duration, sources, speakers, available, _current.Directory);
        Timeline.SetPosition(PlayingSelectedCall ? _player.Position : TimeSpan.Zero);
    }
}
