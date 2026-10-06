using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrameIt.Editing;
using FrameIt.Models;

namespace FrameIt.Services;

public sealed class SessionSnapshot
{
    public string? ActiveId { get; init; }

    public int NextCaptureNumber { get; init; } = 1;

    public List<SessionTabRecord> Tabs { get; init; } = new();
}

public sealed class SessionTabRecord
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public CaptureMode CaptureSource { get; set; }

    public string SourceToken { get; set; } = "region";

    public string? FilePath { get; set; }

    public bool Dirty { get; set; }

    public List<Annotation> Annotations { get; set; } = new();

    public List<Redaction> Redactions { get; set; } = new();
}

public sealed class SessionManifest
{
    public int ActiveIndex { get; set; }

    public int NextCaptureNumber { get; set; } = 1;

    public bool DirtyShutdown { get; set; }

    public List<string> Tabs { get; set; } = new();
}

/// <summary>
/// Recovery files under %LOCALAPPDATA%\FrameIt\sessions. These are not the user's capture folder.
/// </summary>
public sealed class SessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _root;
    private readonly object _gate = new();

    public SessionStore(string? root = null)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            root = Path.Combine(local, "FrameIt", "sessions");
        }

        _root = Path.GetFullPath(root);
    }

    public string Root => _root;

    public string LockPath => Path.Combine(_root, "session.lock");

    public string ManifestPath => Path.Combine(_root, "session.json");

    public string TabsRoot => Path.Combine(_root, "tabs");

    public bool BeginSession()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(TabsRoot);
            DeleteTempFiles();
            var manifest = ReadManifest();
            var unclean = File.Exists(LockPath) || manifest.DirtyShutdown;
            Housekeep(manifest.Tabs);
            File.WriteAllText(LockPath, DateTimeOffset.UtcNow.ToString("O") + " pid=" + Environment.ProcessId);
            manifest.DirtyShutdown = true;
            WriteManifest(manifest);
            return unclean;
        }
    }

    public void MarkCleanShutdown()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_root);
            var manifest = ReadManifest();
            manifest.DirtyShutdown = false;
            WriteManifest(manifest);
            TryDelete(LockPath);
        }
    }

    public SessionSnapshot LoadSnapshot()
    {
        lock (_gate)
        {
            var manifest = ReadManifest();
            var tabs = new List<SessionTabRecord>();
            foreach (var id in manifest.Tabs)
            {
                if (!TryReadTab(id, out var record) || !File.Exists(ImagePath(id)))
                {
                    continue;
                }

                tabs.Add(record);
            }

            var activeId = manifest.ActiveIndex >= 0 && manifest.ActiveIndex < manifest.Tabs.Count
                ? manifest.Tabs[manifest.ActiveIndex]
                : null;
            if (activeId is not null && tabs.All(tab => !string.Equals(tab.Id, activeId, StringComparison.OrdinalIgnoreCase)))
            {
                activeId = tabs.Count > 0 ? tabs[0].Id : null;
            }

            return new SessionSnapshot
            {
                ActiveId = activeId,
                NextCaptureNumber = Math.Max(1, manifest.NextCaptureNumber),
                Tabs = tabs
            };
        }
    }

    public void SaveTab(SessionTabRecord record, Bitmap? image, bool writeImage)
    {
        if (!IsSafeId(record.Id))
        {
            throw new InvalidOperationException("Session tab id is not a file name.");
        }

        lock (_gate)
        {
            var directory = TabDirectory(record.Id);
            Directory.CreateDirectory(directory);
            if (writeImage)
            {
                if (image is null)
                {
                    throw new InvalidOperationException("A session image is required.");
                }

                AtomicWritePng(ImagePath(record.Id), image);
            }

            AtomicWriteText(MetaPath(record.Id), JsonSerializer.Serialize(ToDocument(record), JsonOptions));
        }
    }

    public void WriteThumbnail(string id, Bitmap thumbnail)
    {
        if (!IsSafeId(id))
        {
            return;
        }

        lock (_gate)
        {
            Directory.CreateDirectory(TabDirectory(id));
            AtomicWritePng(ThumbPath(id), thumbnail);
        }
    }

    public string ThumbPathFor(string id) => ThumbPath(id);

    public Bitmap LoadImage(string id)
    {
        if (!IsSafeId(id))
        {
            throw new InvalidOperationException("Session tab id is not a file name.");
        }

        var path = ImagePath(id);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var loaded = new Bitmap(stream);
        return CloneArgb(loaded);
    }

    public void WriteManifest(IReadOnlyList<string> tabIds, int activeIndex, int nextCaptureNumber, bool dirtyShutdown)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_root);
            WriteManifest(new SessionManifest
            {
                Tabs = tabIds.ToList(),
                ActiveIndex = activeIndex,
                NextCaptureNumber = Math.Max(1, nextCaptureNumber),
                DirtyShutdown = dirtyShutdown
            });
        }
    }

    public void DeleteTab(string id)
    {
        if (!IsSafeId(id))
        {
            return;
        }

        lock (_gate)
        {
            var directory = TabDirectory(id);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    public long ClearTabFiles()
    {
        lock (_gate)
        {
            long bytes = 0;
            if (Directory.Exists(TabsRoot))
            {
                foreach (var file in Directory.GetFiles(TabsRoot, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        bytes += new FileInfo(file).Length;
                    }
                    catch (IOException)
                    {
                    }
                }

                Directory.Delete(TabsRoot, recursive: true);
            }

            Directory.CreateDirectory(TabsRoot);
            var manifest = ReadManifest();
            manifest.Tabs.Clear();
            manifest.ActiveIndex = 0;
            manifest.DirtyShutdown = true;
            WriteManifest(manifest);
            return bytes;
        }
    }

    public static Bitmap CreateThumbnail(Bitmap source, int maxEdge = 96)
    {
        var longest = Math.Max(source.Width, source.Height);
        var scale = longest <= 0 ? 1d : Math.Min(1d, maxEdge / (double)longest);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var thumb = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        thumb.SetResolution(96, 96);
        using var graphics = Graphics.FromImage(thumb);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        return thumb;
    }

    public static string SourceToken(CaptureMode mode)
    {
        return mode switch
        {
            CaptureMode.ActiveWindow => "window",
            CaptureMode.FullScreen => "fullscreen",
            CaptureMode.FixedRegion => "fixed",
            _ => "region"
        };
    }

    public static CaptureMode SourceFromToken(string? token, CaptureMode fallback)
    {
        return token switch
        {
            "window" => CaptureMode.ActiveWindow,
            "fullscreen" => CaptureMode.FullScreen,
            "fixed" => CaptureMode.FixedRegion,
            "region" => CaptureMode.Region,
            _ => fallback
        };
    }

    private void Housekeep(IReadOnlyList<string> liveIds)
    {
        if (!Directory.Exists(TabsRoot))
        {
            return;
        }

        var live = liveIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = new List<SessionHousekeeping.FileEntry>();
        foreach (var directory in Directory.GetDirectories(TabsRoot))
        {
            var id = Path.GetFileName(directory);
            foreach (var file in Directory.GetFiles(directory))
            {
                FileInfo info;
                try
                {
                    info = new FileInfo(file);
                }
                catch (IOException)
                {
                    continue;
                }

                files.Add(new SessionHousekeeping.FileEntry(info.FullName, id, info.Length, info.LastWriteTimeUtc));
            }
        }

        foreach (var path in SessionHousekeeping.SelectDeletions(files, live, DateTime.UtcNow))
        {
            TryDelete(path);
        }

        foreach (var directory in Directory.GetDirectories(TabsRoot))
        {
            var id = Path.GetFileName(directory);
            if (live.Contains(id))
            {
                continue;
            }

            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                try
                {
                    Directory.Delete(directory);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private void DeleteTempFiles()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        IEnumerable<string> temps;
        try
        {
            temps = Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories);
        }
        catch (IOException)
        {
            return;
        }

        foreach (var file in temps)
        {
            TryDelete(file);
        }
    }

    private bool TryReadTab(string id, out SessionTabRecord record)
    {
        record = new SessionTabRecord();
        if (!IsSafeId(id) || !File.Exists(MetaPath(id)))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(MetaPath(id));
            var document = JsonSerializer.Deserialize<TabDocument>(json, JsonOptions);
            if (document is null || !string.Equals(document.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            record = FromDocument(document);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return false;
        }
    }

    private SessionManifest ReadManifest()
    {
        if (!File.Exists(ManifestPath))
        {
            return new SessionManifest();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<SessionManifest>(File.ReadAllText(ManifestPath), JsonOptions);
            if (parsed is null)
            {
                return new SessionManifest();
            }

            parsed.Tabs ??= new List<string>();
            parsed.Tabs = parsed.Tabs.Where(IsSafeId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (parsed.NextCaptureNumber < 1)
            {
                parsed.NextCaptureNumber = 1;
            }

            if (parsed.ActiveIndex < 0)
            {
                parsed.ActiveIndex = 0;
            }

            return parsed;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new SessionManifest();
        }
    }

    private void WriteManifest(SessionManifest manifest)
    {
        manifest.Tabs ??= new List<string>();
        AtomicWriteText(ManifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private string TabDirectory(string id)
    {
        var directory = Path.GetFullPath(Path.Combine(TabsRoot, id));
        var root = Path.GetFullPath(TabsRoot);
        if (!directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to touch a path outside the session folder.");
        }

        return directory;
    }

    private string ImagePath(string id) => Path.Combine(TabDirectory(id), "image.png");

    private string ThumbPath(string id) => Path.Combine(TabDirectory(id), "thumb.png");

    private string MetaPath(string id) => Path.Combine(TabDirectory(id), "tab.json");

    private void AtomicWriteText(string path, string contents)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }

    private static void AtomicWritePng(string path, Bitmap bitmap)
    {
        var temp = path + ".tmp";
        bitmap.Save(temp, ImageFormat.Png);
        File.Move(temp, path, overwrite: true);
    }

    private void TryDelete(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (File.Exists(full))
            {
                File.Delete(full);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool IsSafeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64)
        {
            return false;
        }

        foreach (var ch in id)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static Bitmap CloneArgb(Bitmap source)
    {
        var clone = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        clone.SetResolution(96, 96);
        using var graphics = Graphics.FromImage(clone);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
        return clone;
    }

    private static TabDocument ToDocument(SessionTabRecord record)
    {
        return new TabDocument
        {
            Id = record.Id,
            Name = record.Name,
            CreatedUtc = record.CreatedUtc,
            CaptureSource = string.IsNullOrWhiteSpace(record.SourceToken)
                ? SourceToken(record.CaptureSource)
                : record.SourceToken,
            FilePath = record.FilePath,
            Dirty = record.Dirty,
            Annotations = record.Annotations.Select(ToAnnotation).ToList(),
            Redactions = record.Redactions.Select(ToRedaction).ToList()
        };
    }

    private static SessionTabRecord FromDocument(TabDocument document)
    {
        var source = SourceFromToken(document.CaptureSource, CaptureMode.Region);
        return new SessionTabRecord
        {
            Id = document.Id,
            Name = string.IsNullOrWhiteSpace(document.Name) ? "Capture" : document.Name,
            CreatedUtc = document.CreatedUtc,
            CaptureSource = source,
            SourceToken = SourceToken(source),
            FilePath = string.IsNullOrWhiteSpace(document.FilePath) ? null : document.FilePath,
            Dirty = document.Dirty,
            Annotations = document.Annotations?.Select(FromAnnotation).ToList() ?? new List<Annotation>(),
            Redactions = document.Redactions?.Select(FromRedaction).ToList() ?? new List<Redaction>()
        };
    }

    private static AnnotationDto ToAnnotation(Annotation annotation)
    {
        return new AnnotationDto
        {
            Kind = annotation.Kind.ToString(),
            Color = annotation.Color.ToArgb(),
            Thickness = annotation.Thickness,
            FontSize = annotation.FontSize,
            X = annotation.X,
            Y = annotation.Y,
            Width = annotation.Width,
            Height = annotation.Height,
            X2 = annotation.X2,
            Y2 = annotation.Y2,
            Points = annotation.Points.Select(point => new PointDto { X = point.X, Y = point.Y }).ToList(),
            Text = annotation.Text,
            StepNumber = annotation.StepNumber,
            TextWidth = annotation.TextWidth,
            TextHeight = annotation.TextHeight
        };
    }

    private static Annotation FromAnnotation(AnnotationDto dto)
    {
        if (!Enum.TryParse<AnnotationKind>(dto.Kind, out var kind))
        {
            kind = AnnotationKind.Pen;
        }

        return new Annotation
        {
            Kind = kind,
            Color = Color.FromArgb(dto.Color),
            Thickness = dto.Thickness,
            FontSize = dto.FontSize <= 0 ? 18 : dto.FontSize,
            X = dto.X,
            Y = dto.Y,
            Width = dto.Width,
            Height = dto.Height,
            X2 = dto.X2,
            Y2 = dto.Y2,
            Points = dto.Points?.Select(point => new PointD(point.X, point.Y)).ToList() ?? new List<PointD>(),
            Text = dto.Text ?? string.Empty,
            StepNumber = dto.StepNumber,
            TextWidth = dto.TextWidth,
            TextHeight = dto.TextHeight
        };
    }

    private static RedactionDto ToRedaction(Redaction redaction)
    {
        return new RedactionDto
        {
            X = redaction.X,
            Y = redaction.Y,
            Width = redaction.Width,
            Height = redaction.Height,
            Pixelate = redaction.Pixelate,
            Strength = redaction.Strength
        };
    }

    private static Redaction FromRedaction(RedactionDto dto)
    {
        return new Redaction
        {
            X = dto.X,
            Y = dto.Y,
            Width = dto.Width,
            Height = dto.Height,
            Pixelate = dto.Pixelate,
            Strength = dto.Strength is < 1 or > 20 ? 8 : dto.Strength
        };
    }

    private sealed class TabDocument
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public DateTimeOffset CreatedUtc { get; set; }

        public string CaptureSource { get; set; } = "region";

        public string? FilePath { get; set; }

        public bool Dirty { get; set; }

        public List<AnnotationDto>? Annotations { get; set; }

        public List<RedactionDto>? Redactions { get; set; }
    }

    private sealed class AnnotationDto
    {
        public string Kind { get; set; } = "Pen";

        public int Color { get; set; }

        public float Thickness { get; set; }

        public float FontSize { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public double X2 { get; set; }

        public double Y2 { get; set; }

        public List<PointDto>? Points { get; set; }

        public string? Text { get; set; }

        public int StepNumber { get; set; }

        public double TextWidth { get; set; }

        public double TextHeight { get; set; }
    }

    private sealed class PointDto
    {
        public double X { get; set; }

        public double Y { get; set; }
    }

    private sealed class RedactionDto
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public bool Pixelate { get; set; }

        public int Strength { get; set; }
    }
}
