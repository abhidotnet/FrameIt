using FrameItSnap.Services;

// Pure overlap math. No window, no bitmap, no scroll message.

const int width = 8;

var plain = Exact();
Add(plain, Frame(10, 11, 12, 13, 14, 15, 16, 17));
Expect(plain.Add(Frame(13, 14, 15, 16, 17, 18, 19, 20)).Status == ScrollStitch.AddStatus.Extended, "a 3-row scroll extends");
ExpectCodes(Codes(plain.Build()), new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 }, "plain scroll keeps every row in order");

var sticky = Exact();
Add(sticky, Frame(1, 1, 10, 11, 12, 13, 2, 2));
var stickyAdd = sticky.Add(Frame(1, 1, 12, 13, 14, 15, 2, 2));
Expect(stickyAdd.Status == ScrollStitch.AddStatus.Extended && stickyAdd.ScrollRows == 2, "sticky pair scrolls by 2");
Add(sticky, Frame(1, 1, 14, 15, 16, 17, 2, 2));
ExpectCodes(
    Codes(sticky.Build()),
    new byte[] { 1, 1, 10, 11, 12, 13, 14, 15, 16, 17, 2, 2 },
    "header and footer are kept once across three frames");
Expect(Count(Codes(sticky.Build()), 1) == 2, "header rows are not repeated");
Expect(Count(Codes(sticky.Build()), 2) == 2, "footer rows are not repeated");

var blanks = Exact();
Add(blanks, Frame(10, 11, 12, 0, 0));
Add(blanks, Frame(11, 12, 0, 0, 0));
Add(blanks, Frame(12, 0, 0, 0, 0));
ExpectCodes(Codes(blanks.Build()), new byte[] { 10, 11, 12, 0, 0, 0, 0 }, "a run of blank rows is not collapsed or doubled");

var repeated = Exact();
Add(repeated, Frame(10, 10, 11, 12, 13));
Add(repeated, Frame(10, 11, 12, 13, 14));
ExpectCodes(Codes(repeated.Build()), new byte[] { 10, 10, 11, 12, 13, 14 }, "a repeated row is not eaten as a sticky header");

var steps = Exact();
Add(steps, Frame(10, 11, 12, 13, 14, 15, 16, 17));
Add(steps, Frame(13, 14, 15, 16, 17, 18, 19, 20));
Add(steps, Frame(15, 16, 17, 18, 19, 20, 21, 22));
ExpectCodes(Codes(steps.Build()), new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22 }, "scroll distance can change between frames");

var same = Exact();
Add(same, Frame(10, 11, 12, 13));
var unchanged = same.Add(Frame(10, 11, 12, 13));
Expect(unchanged.Status == ScrollStitch.AddStatus.Unchanged, "an identical frame is not a scroll");
ExpectCodes(Codes(same.Build()), new byte[] { 10, 11, 12, 13 }, "an identical frame leaves the image alone");

var different = Exact();
Add(different, Frame(10, 11, 12, 13, 14));
var unaligned = different.Add(Frame(20, 21, 22, 23, 24));
Expect(unaligned.Status == ScrollStitch.AddStatus.Unaligned, "frames with no shared rows do not stitch");
ExpectCodes(Codes(different.Build()), new byte[] { 10, 11, 12, 13, 14 }, "a rejected frame is not appended");

var narrow = Exact();
Add(narrow, Frame(10, 11, 12, 13));
var widthMismatch = narrow.Add(new ScrollStitch.FrameView(width + 1, 4, new byte[(width + 1) * 4 * 4]));
Expect(widthMismatch.Status == ScrollStitch.AddStatus.Unaligned, "a different width is rejected");

var capped = Exact(maxResultHeight: 12);
Add(capped, Frame(10, 11, 12, 13, 14, 15, 16, 17));
Add(capped, Frame(13, 14, 15, 16, 17, 18, 19, 20));
var cappedAdd = capped.Add(Frame(16, 17, 18, 19, 20, 21, 22, 23));
Expect(cappedAdd.Status == ScrollStitch.AddStatus.HeightCapped, "the height cap stops the stitch");
Expect(capped.HitHeightCap, "the builder records the cap");
var cappedCodes = Codes(capped.Build());
Expect(cappedCodes.Length == 12, "the capped image is 12 rows");
ExpectCodes(cappedCodes, new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21 }, "the cap keeps the top of the scroll");

var firstOnly = Exact();
Add(firstOnly, Frame(4, 5, 6));
ExpectCodes(Codes(firstOnly.Build()), new byte[] { 4, 5, 6 }, "one frame is copied unchanged");

var overTall = Exact(maxResultHeight: 3);
var tallStatus = overTall.Add(Frame(1, 2, 3, 4, 5));
Expect(tallStatus.Status == ScrollStitch.AddStatus.HeightCapped, "a single frame taller than the cap is trimmed");
ExpectCodes(Codes(overTall.Build()), new byte[] { 1, 2, 3 }, "the trim keeps the top rows");

var fuzzy = new ScrollStitch.Builder(
    channelTolerance: 16,
    pixelMatchRatio: 1,
    rowMatchRatio: 1,
    minOverlapRows: 2,
    sampleCount: int.MaxValue);
Add(fuzzy, Frame(10, 11, 12, 13));
var within = fuzzy.Add(FrameOffset(8, 10, 11, 12, 13));
Expect(within.Status == ScrollStitch.AddStatus.Unchanged, "channel noise inside the tolerance is not motion");

var exactNoise = Exact();
Add(exactNoise, Frame(10, 11, 12, 13));
var noise = exactNoise.Add(FrameOffset(8, 10, 11, 12, 13));
Expect(noise.Status == ScrollStitch.AddStatus.Unaligned, "the same noise with zero tolerance does not match");

var measured = ScrollStitch.Measure(
    Frame(1, 1, 10, 11, 12, 13, 2, 2).Pixels,
    Frame(1, 1, 12, 13, 14, 15, 2, 2).Pixels,
    width,
    8,
    channelTolerance: 0,
    pixelMatchRatio: 1,
    rowMatchRatio: 1,
    minOverlapRows: 2,
    sampleCount: int.MaxValue);
Expect(measured.Moved && measured.ScrollRows == 2 && measured.HeaderRows == 2 && measured.FooterRows == 2, "measure reports the sticky bars and the shift");

var page = Exact();
Add(page, Frame(10, 11, 12, 13, 14, 15, 16, 17));
var paged = page.Add(Frame(16, 17, 18, 19, 20, 21, 22, 23));
Expect(paged.Status == ScrollStitch.AddStatus.Extended && paged.ScrollRows == 6, "a long page step still stitches when two rows overlap");
ExpectCodes(Codes(page.Build()), new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23 }, "the long step does not drop the overlap");

Console.WriteLine("scroll stitch checks passed");

static ScrollStitch.Builder Exact(int maxResultHeight = ScrollStitch.MaxResultHeight)
{
    return new ScrollStitch.Builder(
        channelTolerance: 0,
        pixelMatchRatio: 1,
        rowMatchRatio: 1,
        minOverlapRows: 2,
        sampleCount: int.MaxValue,
        maxResultHeight: maxResultHeight);
}

static void Add(ScrollStitch.Builder builder, ScrollStitch.FrameView frame)
{
    var result = builder.Add(frame);
    if (result.Status is ScrollStitch.AddStatus.Unaligned)
    {
        throw new InvalidOperationException("a fixture frame was rejected");
    }
}

static ScrollStitch.FrameView Frame(params byte[] codes)
{
    return Paint(codes, static code => code);
}

static ScrollStitch.FrameView FrameOffset(byte delta, params byte[] codes)
{
    return Paint(codes, code => (byte)(code + delta));
}

static ScrollStitch.FrameView Paint(byte[] codes, Func<byte, byte> map)
{
    var pixels = new byte[codes.Length * width * 4];
    for (var y = 0; y < codes.Length; y++)
    {
        var code = map(codes[y]);
        for (var x = 0; x < width; x++)
        {
            var i = ((y * width) + x) * 4;
            pixels[i] = code;
            pixels[i + 1] = code;
            pixels[i + 2] = (byte)(255 - code);
            pixels[i + 3] = 255;
        }
    }

    return new ScrollStitch.FrameView(width, codes.Length, pixels);
}

static byte[] Codes(ScrollStitch.FrameView frame)
{
    var codes = new byte[frame.Height];
    for (var y = 0; y < frame.Height; y++)
    {
        codes[y] = frame.Pixels[y * frame.Width * 4];
    }

    return codes;
}

static int Count(byte[] codes, byte value)
{
    var count = 0;
    foreach (var code in codes)
    {
        if (code == value)
        {
            count++;
        }
    }

    return count;
}

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void ExpectCodes(byte[] actual, byte[] expected, string message)
{
    if (actual.Length != expected.Length || !actual.AsSpan().SequenceEqual(expected))
    {
        throw new InvalidOperationException(message + " actual=[" + string.Join(",", actual) + "]");
    }
}
