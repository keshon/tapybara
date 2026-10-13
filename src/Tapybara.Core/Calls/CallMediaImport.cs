using System.Diagnostics;
using NAudio.Wave;

namespace Tapybara.Core.Calls;

/// <summary>Convert one media file into an owned, single-track session.</summary>
public static class CallMediaImport
{
    public static async Task<CallSession> ImportAsync(string source, string root,
        string? language = null, int? speakerCount = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("Media file no longer exists.", source);
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        string staging = Path.Combine(root, ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            string output = Path.Combine(staging, CallSession.SystemFileName);
            try
            {
                await Task.Run(() => Tapybara.Core.Audio.AudioFile.ConvertToWav16k(source, output, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException)
            {
                await ConvertWithFfmpegAsync(source, output, cancellationToken).ConfigureAwait(false);
            }
            TimeSpan duration;
            using (var audio = new WaveFileReader(output)) duration = audio.TotalTime;
            if (duration <= TimeSpan.Zero) throw new IOException("The file contains no audio.");
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.Now;
            string directory = Path.Combine(root, CallSession.BuildFolderName(now, null) + " import-" + Guid.NewGuid().ToString("N"));
            var session = new CallSession { Directory = staging, StartedAt = now, Duration = duration,
                Title = Path.GetFileNameWithoutExtension(source), ImportedMedia = true, ImportLanguage = language, SpeakerCountHint = speakerCount };
            CallMeta.Save(session);
            if (CallMeta.Load(staging) is null) throw new IOException("Could not save imported session.");
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(staging, directory);
            return session with { Directory = directory };
        }
        finally
        {
            // This unique staging directory belongs only to this import.
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static async Task ConvertWithFfmpegAsync(string source, string output, CancellationToken cancellationToken)
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        var start = new ProcessStartInfo(File.Exists(bundled) ? bundled : "ffmpeg") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (string arg in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", Path.GetFullPath(source),
            "-map", "0:a:0", "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", output })
            start.ArgumentList.Add(arg);
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(start) ?? throw new IOException("Could not start media conversion.");
        using var stop = cancellationToken.Register(() => {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        });
        Task<string> errors = ReadErrorsAsync(process.StandardError);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        string error = await errors.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new IOException("Could not decode media. " + error[..Math.Min(error.Length, 1200)]);
    }
    private static async Task<string> ReadErrorsAsync(StreamReader reader)
    {
        var result = new System.Text.StringBuilder();
        char[] buffer = new char[1024];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false)) > 0)
        {
            int keep = Math.Min(read, 1200 - result.Length);
            if (keep > 0) result.Append(buffer, 0, keep);
        }
        return result.ToString();
    }

}
