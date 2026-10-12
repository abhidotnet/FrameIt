namespace FrameItSnap.Services;

/// <summary>
/// Stitches vertical scrolling-capture frames. Each frame is the client area in
/// physical pixels (tightly packed BGRA). Overlap is the shift where rows match.
/// Rows that stay put at the top or bottom are a sticky header or footer and are
/// emitted once.
/// </summary>
public static class ScrollStitch
{
    public const int MaxResultHeight = 20_000;

    public const int DefaultChannelTolerance = 24;

    public const double DefaultPixelMatchRatio = 0.90;

    public const double DefaultRowMatchRatio = 0.95;

    public const int DefaultMinOverlapRows = 6;

    public const int DefaultSampleCount = 64;

    public readonly record struct FrameView(int Width, int Height, byte[] Pixels);

    public enum AddStatus
    {
        Started,
        Extended,
        Unchanged,
        Unaligned,
        HeightCapped
    }

    public readonly record struct AddResult(AddStatus Status, int ScrollRows, int ResultHeight);

    public readonly record struct OverlapMatch(
        bool Moved,
        int ScrollRows,
        int HeaderRows,
        int FooterRows,
        int MatchedRows,
        int ComparedRows);

    public static OverlapMatch Measure(
        ReadOnlySpan<byte> previous,
        ReadOnlySpan<byte> next,
        int width,
        int height,
        int channelTolerance = 0,
        double pixelMatchRatio = 1,
        double rowMatchRatio = 1,
        int minOverlapRows = 2,
        int sampleCount = int.MaxValue)
    {
        if (width < 1 || height < 1)
        {
            return default;
        }

        var stride = width * 4;
        if (previous.Length < stride * height || next.Length < stride * height)
        {
            return default;
        }

        var samples = BuildSamples(width, sampleCount);
        var allowedPixelMisses = AllowedMisses(samples.Length, pixelMatchRatio);
        var view = new PairView(previous, next, width, height, samples, channelTolerance, allowedPixelMisses);

        var sameRows = 0;
        for (var y = 0; y < height; y++)
        {
            if (view.RowsEqual(y, y))
            {
                sameRows++;
            }
        }

        // A caret or a one-pixel clock tick must not look like a scroll.
        if (sameRows == height || sameRows * 1000 >= height * 998)
        {
            return default;
        }

        var maxSticky = Math.Max(0, (int)Math.Floor(height * 0.45));
        var prefix = 0;
        while (prefix < height && prefix < maxSticky && view.RowsEqual(prefix, prefix))
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < height - prefix && suffix < maxSticky && view.RowsEqual(height - 1 - suffix, height - 1 - suffix))
        {
            suffix++;
        }

        var best = new OverlapMatch(false, 0, 0, 0, -1, 0);
        Consider(view, prefix, suffix, rowMatchRatio, minOverlapRows, ref best);
        if (suffix != 0)
        {
            Consider(view, prefix, 0, rowMatchRatio, minOverlapRows, ref best);
        }

        if (prefix != 0)
        {
            Consider(view, 0, suffix, rowMatchRatio, minOverlapRows, ref best);
        }

        if (prefix != 0 || suffix != 0)
        {
            Consider(view, 0, 0, rowMatchRatio, minOverlapRows, ref best);
        }

        // Shrink only when the full bars do not already explain the scroll.
        // Neighboring lines of the same color can make a bar a few rows too tall.
        if (RatioMilli(best.MatchedRows, best.ComparedRows) < 980)
        {
            for (var footer = suffix - 1; footer >= 1; footer--)
            {
                Consider(view, prefix, footer, rowMatchRatio, minOverlapRows, ref best);
            }

            for (var header = prefix - 1; header >= 1; header--)
            {
                Consider(view, header, suffix, rowMatchRatio, minOverlapRows, ref best);
            }
        }

        return best.Moved ? best : default;
    }

    private static void Consider(
        PairView view,
        int header,
        int footer,
        double rowMatchRatio,
        int minOverlapRows,
        ref OverlapMatch best)
    {
        if (TryBestShift(view, header, footer, rowMatchRatio, minOverlapRows, out var shift) &&
            Prefer(shift, best))
        {
            best = shift;
        }
    }

    public sealed class Builder
    {
        private readonly int _channelTolerance;
        private readonly double _pixelMatchRatio;
        private readonly double _rowMatchRatio;
        private readonly int _minOverlapRows;
        private readonly int _sampleCount;
        private readonly int _maxResultHeight;
        private readonly List<byte[]> _rows = new();
        private byte[]? _last;
        private byte[][]? _footer;
        private int _width;
        private int _frameHeight;
        private int _headerRows;
        private int _footerRows;
        private bool _stickyLocked;
        private bool _hitHeightCap;

        public Builder(
            int channelTolerance = DefaultChannelTolerance,
            double pixelMatchRatio = DefaultPixelMatchRatio,
            double rowMatchRatio = DefaultRowMatchRatio,
            int minOverlapRows = DefaultMinOverlapRows,
            int sampleCount = DefaultSampleCount,
            int maxResultHeight = MaxResultHeight)
        {
            if (maxResultHeight < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxResultHeight));
            }

            _channelTolerance = Math.Max(0, channelTolerance);
            _pixelMatchRatio = pixelMatchRatio;
            _rowMatchRatio = rowMatchRatio;
            _minOverlapRows = Math.Max(1, minOverlapRows);
            _sampleCount = sampleCount;
            _maxResultHeight = maxResultHeight;
        }

        public int FrameCount { get; private set; }

        public bool HitHeightCap => _hitHeightCap;

        public int ResultHeight => Math.Min(_maxResultHeight, _rows.Count + (_footer?.Length ?? 0));

        public AddResult Add(FrameView frame)
        {
            if (frame.Width < 1 || frame.Height < 1 || frame.Pixels is null || frame.Pixels.Length < frame.Width * frame.Height * 4)
            {
                return new AddResult(AddStatus.Unaligned, 0, ResultHeight);
            }

            if (_width == 0)
            {
                return Start(frame);
            }

            if (frame.Width != _width || frame.Height != _frameHeight || _last is null)
            {
                return new AddResult(AddStatus.Unaligned, 0, ResultHeight);
            }

            var match = Measure(
                _last,
                frame.Pixels,
                _width,
                frame.Height,
                _channelTolerance,
                _pixelMatchRatio,
                _rowMatchRatio,
                _minOverlapRows,
                _sampleCount);

            if (!match.Moved)
            {
                var unchanged = IsUnchanged(_last, frame.Pixels, _width, frame.Height);
                return new AddResult(unchanged ? AddStatus.Unchanged : AddStatus.Unaligned, 0, ResultHeight);
            }

            if (_stickyLocked && (match.HeaderRows != _headerRows || match.FooterRows != _footerRows))
            {
                // The first pair decided which bars stay put. Ask again with those
                // sizes so a later pair cannot grow the footer and repeat it.
                match = MeasureLocked(
                    _last,
                    frame.Pixels,
                    _width,
                    frame.Height,
                    _headerRows,
                    _footerRows);
                if (!match.Moved)
                {
                    return new AddResult(AddStatus.Unaligned, 0, ResultHeight);
                }
            }

            if (!_stickyLocked)
            {
                _headerRows = match.HeaderRows;
                _footerRows = match.FooterRows;
                var strip = Math.Min(_footerRows, Math.Max(0, _rows.Count - Math.Max(1, _headerRows)));
                if (strip > 0)
                {
                    _rows.RemoveRange(_rows.Count - strip, strip);
                }

                _stickyLocked = true;
            }

            var appended = AppendNewRows(frame, match.ScrollRows, match.FooterRows);
            RememberFooter(frame, match.FooterRows);
            _last = CopyPixels(frame);
            FrameCount++;
            if (appended < match.ScrollRows)
            {
                _hitHeightCap = true;
                return new AddResult(AddStatus.HeightCapped, appended, ResultHeight);
            }

            return new AddResult(AddStatus.Extended, appended, ResultHeight);
        }

        public FrameView Build()
        {
            if (_width < 1 || _rows.Count == 0)
            {
                return new FrameView(0, 0, Array.Empty<byte>());
            }

            var footer = _footer ?? Array.Empty<byte[]>();
            var footerCount = footer.Length;
            var bodyCount = _rows.Count;
            if (bodyCount + footerCount > _maxResultHeight)
            {
                bodyCount = Math.Max(0, _maxResultHeight - footerCount);
                if (bodyCount + footerCount > _maxResultHeight)
                {
                    footerCount = Math.Max(0, _maxResultHeight - bodyCount);
                }
            }

            var height = bodyCount + footerCount;
            var pixels = new byte[height * _width * 4];
            for (var y = 0; y < bodyCount; y++)
            {
                Buffer.BlockCopy(_rows[y], 0, pixels, y * _width * 4, _width * 4);
            }

            for (var i = 0; i < footerCount; i++)
            {
                Buffer.BlockCopy(footer[i], 0, pixels, (bodyCount + i) * _width * 4, _width * 4);
            }

            return new FrameView(_width, height, pixels);
        }

        private AddResult Start(FrameView frame)
        {
            _width = frame.Width;
            _frameHeight = frame.Height;
            _last = CopyPixels(frame);
            var rows = Math.Min(frame.Height, _maxResultHeight);
            for (var y = 0; y < rows; y++)
            {
                _rows.Add(CopyRow(frame, y));
            }

            FrameCount = 1;
            if (rows < frame.Height)
            {
                _hitHeightCap = true;
                return new AddResult(AddStatus.HeightCapped, 0, ResultHeight);
            }

            return new AddResult(AddStatus.Started, 0, ResultHeight);
        }

        private int AppendNewRows(FrameView frame, int scrollRows, int footerRows)
        {
            var start = frame.Height - footerRows - scrollRows;
            if (start < 0)
            {
                start = 0;
            }

            var room = _maxResultHeight - _rows.Count - footerRows;
            if (room < 0)
            {
                room = 0;
            }

            var take = Math.Min(scrollRows, room);
            for (var i = 0; i < take; i++)
            {
                var y = start + i;
                if (y < 0 || y >= frame.Height)
                {
                    break;
                }

                _rows.Add(CopyRow(frame, y));
            }

            return take;
        }

        private void RememberFooter(FrameView frame, int footerRows)
        {
            if (footerRows < 1)
            {
                _footer = Array.Empty<byte[]>();
                return;
            }

            var rows = new byte[footerRows][];
            for (var i = 0; i < footerRows; i++)
            {
                rows[i] = CopyRow(frame, frame.Height - footerRows + i);
            }

            _footer = rows;
        }

        private OverlapMatch MeasureLocked(byte[] previous, byte[] next, int width, int height, int header, int footer)
        {
            var samples = BuildSamples(width, _sampleCount);
            var view = new PairView(
                previous,
                next,
                width,
                height,
                samples,
                _channelTolerance,
                AllowedMisses(samples.Length, _pixelMatchRatio));
            return TryBestShift(view, header, footer, _rowMatchRatio, _minOverlapRows, out var shift)
                ? shift
                : default;
        }

        private bool IsUnchanged(byte[] previous, byte[] next, int width, int height)
        {
            var samples = BuildSamples(width, _sampleCount);
            var view = new PairView(
                previous,
                next,
                width,
                height,
                samples,
                _channelTolerance,
                AllowedMisses(samples.Length, _pixelMatchRatio));
            var same = 0;
            for (var y = 0; y < height; y++)
            {
                if (view.RowsEqual(y, y))
                {
                    same++;
                }
            }

            return same == height || same * 1000 >= height * 998;
        }

        private static byte[] CopyPixels(FrameView frame)
        {
            var length = frame.Width * frame.Height * 4;
            var copy = new byte[length];
            Buffer.BlockCopy(frame.Pixels, 0, copy, 0, length);
            return copy;
        }

        private static byte[] CopyRow(FrameView frame, int y)
        {
            var row = new byte[frame.Width * 4];
            Buffer.BlockCopy(frame.Pixels, y * frame.Width * 4, row, 0, row.Length);
            return row;
        }
    }

    private static bool Prefer(OverlapMatch candidate, OverlapMatch current)
    {
        if (!candidate.Moved)
        {
            return false;
        }

        if (!current.Moved || current.MatchedRows < 0)
        {
            return true;
        }

        var candidateRatio = RatioMilli(candidate.MatchedRows, candidate.ComparedRows);
        var currentRatio = RatioMilli(current.MatchedRows, current.ComparedRows);
        if (Math.Abs(candidateRatio - currentRatio) > 20)
        {
            return candidateRatio > currentRatio;
        }

        var candidateSticky = candidate.HeaderRows + candidate.FooterRows;
        var currentSticky = current.HeaderRows + current.FooterRows;
        if (candidateSticky != currentSticky)
        {
            return candidateSticky > currentSticky;
        }

        if (candidate.ScrollRows != current.ScrollRows)
        {
            return candidate.ScrollRows < current.ScrollRows;
        }

        return candidate.MatchedRows > current.MatchedRows;
    }

    private static int RatioMilli(int matched, int compared)
    {
        if (compared < 1)
        {
            return 0;
        }

        return (int)Math.Round(matched * 1000.0 / compared, MidpointRounding.AwayFromZero);
    }

    private static bool TryBestShift(
        PairView view,
        int header,
        int footer,
        double rowMatchRatio,
        int minOverlapRows,
        out OverlapMatch match)
    {
        match = default;
        var found = false;
        var limit = view.Height - header - footer - minOverlapRows;
        for (var scroll = 1; scroll <= limit; scroll++)
        {
            if (!view.TryMiddle(header, footer, scroll, rowMatchRatio, minOverlapRows, out var matched, out var compared))
            {
                continue;
            }

            var candidate = new OverlapMatch(true, scroll, header, footer, matched, compared);
            if (!found || Prefer(candidate, match))
            {
                match = candidate;
                found = true;
            }
        }

        return found;
    }

    private static int AllowedMisses(int samples, double ratio)
    {
        if (samples < 1)
        {
            return 0;
        }

        var clamped = double.IsNaN(ratio) ? 1 : Math.Clamp(ratio, 0, 1);
        return (int)Math.Floor(samples * (1.0 - clamped));
    }

    private static int[] BuildSamples(int width, int sampleCount)
    {
        var count = sampleCount >= width ? width : Math.Max(1, sampleCount);
        var xs = new int[count];
        if (count == width)
        {
            for (var i = 0; i < width; i++)
            {
                xs[i] = i;
            }

            return xs;
        }

        for (var i = 0; i < count; i++)
        {
            xs[i] = (int)((long)i * width / count);
        }

        return xs;
    }

    private readonly ref struct PairView
    {
        private readonly ReadOnlySpan<byte> _previous;
        private readonly ReadOnlySpan<byte> _next;
        private readonly int[] _samples;
        private readonly int _tolerance;
        private readonly int _allowedPixelMisses;

        public PairView(
            ReadOnlySpan<byte> previous,
            ReadOnlySpan<byte> next,
            int width,
            int height,
            int[] samples,
            int tolerance,
            int allowedPixelMisses)
        {
            _previous = previous;
            _next = next;
            Width = width;
            Height = height;
            _samples = samples;
            _tolerance = tolerance;
            _allowedPixelMisses = allowedPixelMisses;
        }

        public int Width { get; }

        public int Height { get; }

        public bool RowsEqual(int previousY, int nextY)
        {
            var previousOffset = previousY * Width * 4;
            var nextOffset = nextY * Width * 4;
            var misses = 0;
            foreach (var x in _samples)
            {
                var a = previousOffset + (x * 4);
                var b = nextOffset + (x * 4);
                if (Math.Abs(_previous[a] - _next[b]) > _tolerance ||
                    Math.Abs(_previous[a + 1] - _next[b + 1]) > _tolerance ||
                    Math.Abs(_previous[a + 2] - _next[b + 2]) > _tolerance)
                {
                    misses++;
                    if (misses > _allowedPixelMisses)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public bool TryMiddle(
            int header,
            int footer,
            int scroll,
            double rowMatchRatio,
            int minOverlapRows,
            out int matched,
            out int compared)
        {
            compared = Height - header - footer - scroll;
            matched = 0;
            if (compared < minOverlapRows || header < 0 || footer < 0 || scroll < 1)
            {
                return false;
            }

            var allowedRowMisses = AllowedMisses(compared, rowMatchRatio);
            var misses = 0;
            for (var i = 0; i < compared; i++)
            {
                var nextY = header + i;
                var previousY = nextY + scroll;
                if (RowsEqual(previousY, nextY))
                {
                    matched++;
                    continue;
                }

                misses++;
                if (misses > allowedRowMisses)
                {
                    matched = 0;
                    return false;
                }
            }

            return true;
        }
    }
}
