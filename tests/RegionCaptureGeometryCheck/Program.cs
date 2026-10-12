using FrameItSnap.Services;

// Two 1920x1080 monitors, X=0 and X=1920, matching a mixed-DPI desk. A third monitor
// sits to the left of the primary so its coordinates are negative.

const double scale100 = 1.0;
const double scale125 = 1.25;
const double scale150 = 1.5;
const double scale175 = 1.75;

var rightWhole = CaptureGeometry.DipToPhysical(1920, 0, scale150, scale150, 0, 0, 1280, 720);
Expect(rightWhole == new CaptureGeometry.PixelRect(1920, 0, 1920, 1080), "150% monitor: a full-screen DIP drag is the physical 1920x1080, not 1280x720");

var rightIfIgnored = CaptureGeometry.DipToPhysical(1920, 0, scale100, scale100, 0, 0, 1280, 720);
Expect(rightIfIgnored.Width == 1280 && rightWhole.Width == 1920, "the right monitor uses its own 150% scale, not the primary 100% scale");

var partial = CaptureGeometry.DipToPhysical(1920, 0, scale150, scale150, 10, 20, 200, 80);
Expect(partial == new CaptureGeometry.PixelRect(1935, 30, 300, 120), "150% partial DIP rectangle scales width and height from that monitor's origin");

var hundred = CaptureGeometry.DipToPhysical(0, 0, scale100, scale100, 10, 20, 100, 50);
Expect(hundred == new CaptureGeometry.PixelRect(10, 20, 100, 50), "100% monitor: DIP size is already the physical size");

var left125 = CaptureGeometry.DipToPhysical(-1920, -1080, scale125, scale125, 8, 4, 80, 40);
Expect(left125 == new CaptureGeometry.PixelRect(-1910, -1075, 100, 50), "125% monitor with a negative origin scales from that origin");

var placed125 = CaptureGeometry.PhysicalToBitmap(left125.X, left125.Y, left125.Width, left125.Height, -1920, -1080, 3840, 2160);
Expect(placed125 == new CaptureGeometry.PixelRect(10, 5, 100, 50), "125% physical rect lands in the virtual bitmap without a second scale");

var dip175W = CaptureGeometry.DipLength(1920, 168);
var dip175H = CaptureGeometry.DipLength(1080, 168);
var third = CaptureGeometry.DipToPhysical(3840, 0, scale175, scale175, 0, 0, dip175W, dip175H);
Expect(third == new CaptureGeometry.PixelRect(3840, 0, 1920, 1080), "175% full monitor round-trips to 1920x1080 physical pixels");

var rounded = CaptureGeometry.DipToPhysical(1920, 0, scale175, scale175, 2, 2, 4, 8);
Expect(rounded == new CaptureGeometry.PixelRect(1924, 4, 7, 14), "175% edges round half away from zero instead of banker's rounding");

var reversed = CaptureGeometry.DipToPhysical(0, 0, scale100, scale100, 100, 50, -40, -10);
Expect(reversed == new CaptureGeometry.PixelRect(60, 40, 40, 10), "a DIP drag with a negative size is normalized before scaling");

var span = CaptureGeometry.PhysicalToBitmap(1600, 100, 600, 200, 0, 0, 3840, 1080);
Expect(span == new CaptureGeometry.PixelRect(1600, 100, 600, 200), "a selection that crosses 100% and 150% keeps its physical width");

var negative = CaptureGeometry.PhysicalToBitmap(-100, 50, 300, 100, -1920, 0, 3840, 1080);
Expect(negative == new CaptureGeometry.PixelRect(1820, 50, 300, 100), "a monitor left of the primary subtracts the negative virtual origin and does not scale");

var clipped = CaptureGeometry.PhysicalToBitmap(-40, -10, 100, 30, 0, 0, 1920, 1080);
Expect(clipped == new CaptureGeometry.PixelRect(0, 0, 60, 20), "a selection that starts off the virtual screen is clipped, not scaled");

Expect(CaptureGeometry.PhysicalToBitmap(2000, 0, 10, 10, 0, 0, 1920, 1080) is null, "a selection outside the bitmap is empty");
Expect(CaptureGeometry.PhysicalToBitmap(0, 0, 0, 10, 0, 0, 1920, 1080) is null, "a zero-width selection is empty");

Expect(CaptureGeometry.DipLength(1920, 96) == 1920, "96 DPI display size is the pixel count");
Expect(CaptureGeometry.DipLength(1920, 120) == 1536, "125% display size is 1920 * 96 / 120");
Expect(CaptureGeometry.DipLength(1920, 144) == 1280, "150% display size is 1920 * 96 / 144");
Expect(Math.Abs(CaptureGeometry.DipLength(1920, 168) - (1920 * 96.0 / 168)) < 0.0001, "175% display size keeps the fractional DIP");
Expect(CaptureGeometry.DipLength(96, 0) == 96, "a missing DPI falls back to 96");
Expect(Math.Abs(CaptureGeometry.DipLength(-1920, 144) - (-1280)) < 0.0001, "a negative offset keeps its sign");

foreach (var dpi in new[] { 96.0, 120.0, 144.0, 168.0 })
{
    var dips = CaptureGeometry.DipLength(1920, dpi);
    var device = dips * dpi / 96.0;
    Expect(Math.Abs(device - 1920) < 0.001, "DIP size times monitor scale is 1920 device pixels at " + dpi + " DPI");
}

ExpectThrows(() => CaptureGeometry.DipToPhysical(0, 0, 0, 1, 0, 0, 1, 1));
ExpectThrows(() => CaptureGeometry.DipToPhysical(0, 0, 1, double.NaN, 0, 0, 1, 1));
ExpectThrows(() => CaptureGeometry.DipToPhysical(0, 0, -1.5, 1.5, 0, 0, 10, 10));

Console.WriteLine("region capture geometry checks passed");

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void ExpectThrows(Action action)
{
    try
    {
        action();
    }
    catch (ArgumentOutOfRangeException)
    {
        return;
    }

    throw new InvalidOperationException("expected an invalid monitor scale to be rejected");
}
