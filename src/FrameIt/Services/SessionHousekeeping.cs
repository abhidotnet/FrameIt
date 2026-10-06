namespace FrameIt.Services;

/// <summary>
/// Decides which session files to drop. Live tabs are kept. Everything else is removed
/// once it is older than 30 days, and sooner when the store is over 200 MB.
/// </summary>
public static class SessionHousekeeping
{
    public const long MaxBytes = 200L * 1024 * 1024;

    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    public readonly record struct FileEntry(string Path, string? TabId, long Length, DateTime LastWriteTimeUtc);

    public static IReadOnlyList<string> SelectDeletions(
        IReadOnlyList<FileEntry> files,
        IReadOnlySet<string> liveTabIds,
        DateTime utcNow,
        long maxBytes = MaxBytes,
        TimeSpan? maxAge = null)
    {
        var cutoff = utcNow - (maxAge ?? MaxAge);
        var delete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in files)
        {
            total += Math.Max(0, file.Length);
        }

        foreach (var file in files)
        {
            if (IsLive(file, liveTabIds) || file.LastWriteTimeUtc >= cutoff)
            {
                continue;
            }

            if (delete.Add(file.Path))
            {
                total -= Math.Max(0, file.Length);
            }
        }

        if (total > maxBytes)
        {
            foreach (var file in files
                         .OrderBy(file => file.LastWriteTimeUtc)
                         .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase))
            {
                if (total <= maxBytes)
                {
                    break;
                }

                if (delete.Contains(file.Path) || IsLive(file, liveTabIds))
                {
                    continue;
                }

                delete.Add(file.Path);
                total -= Math.Max(0, file.Length);
            }
        }

        return delete.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsLive(FileEntry file, IReadOnlySet<string> liveTabIds)
    {
        return file.TabId is not null && liveTabIds.Contains(file.TabId);
    }
}
