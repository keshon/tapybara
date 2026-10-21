namespace Tapybara.Core.Calls;

/// <summary>An independent copy of the session's owned media and editable documents.</summary>
public static class CallSessionCopy
{
    public static async Task<CallSession> DuplicateAsync(string sourceDirectory, string root, string title,
        CancellationToken cancellationToken = default)
    {
        sourceDirectory = Path.GetFullPath(sourceDirectory);
        root = Path.GetFullPath(root);
        cancellationToken.ThrowIfCancellationRequested();
        var source = CallMeta.Load(sourceDirectory) ?? throw new IOException("Could not read the source session.");
        string[] documents = [CallSession.MetaFileName, CallSession.TranscriptDataFileName,
            CallSession.TranscriptFileName, CallLibrary.NoteFileName];
        var snapshot = documents.ToDictionary(name => name, name =>
            File.Exists(Path.Combine(sourceDirectory, name)) ? File.ReadAllBytes(Path.Combine(sourceDirectory, name)) : null);
        Directory.CreateDirectory(root);
        string staging = Path.Combine(root, ".copy-" + Guid.NewGuid().ToString("N"));
        string destination = Path.Combine(root, CallSession.BuildFolderName(source.StartedAt, null) + " copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (string name in new[] { CallSession.MicFileName, CallSession.SystemFileName })
            {
                string path = Path.Combine(sourceDirectory, name);
                if (!File.Exists(path)) { continue; }
                // Holding a read-only share prevents the source recording from being written during the copy.
                await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                await using var output = new FileStream(Path.Combine(staging, name), FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 65536, useAsync: true);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
            foreach ((string name, byte[]? bytes) in snapshot)
            {
                string path = Path.Combine(sourceDirectory, name);
                byte[]? current = File.Exists(path) ? File.ReadAllBytes(path) : null;
                if (!(bytes is null ? current is null : current is not null && bytes.AsSpan().SequenceEqual(current)))
                {
                    throw new IOException("The session changed while copying. Please try again.");
                }
                if (bytes is not null)
                {
                    await File.WriteAllBytesAsync(Path.Combine(staging, name), bytes, cancellationToken).ConfigureAwait(false);
                }
            }
            var saved = CallMeta.Load(staging) ?? throw new IOException("Could not read the copied session.");
            var copy = saved with { Directory = staging, Title = title };
            CallMeta.Save(copy);
            if (CallMeta.Load(staging)?.Title != title) { throw new IOException("Could not save the copied session."); }
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
            return copy with { Directory = destination };
        }
        finally
        {
            // Both paths are unique children of the resolved calls root, owned by this operation.
            if (Path.GetDirectoryName(staging) == root && Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }
}
