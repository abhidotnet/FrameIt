using FrameIt.Services;

var now = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "live" };
const long mb = 1024L * 1024L;

var oldOrphan = Entry("old-orphan", "orphan-old", 10 * mb, now.AddDays(-40));
var youngOrphan = Entry("young-orphan", "orphan-young", 10 * mb, now.AddDays(-1));
var oldLive = Entry("old-live", "live", 10 * mb, now.AddDays(-40));
var kept = SessionHousekeeping.SelectDeletions(new[] { oldOrphan, youngOrphan, oldLive }, live, now);
Expect(kept.Count == 1 && kept[0] == "old-orphan", "age removes non-live files and keeps the open tab");

var underCap = SessionHousekeeping.SelectDeletions(new[] { youngOrphan }, live, now);
Expect(underCap.Count == 0, "a young recovery file stays while the folder is under 200 MB");

var older = Entry("a-older", "orphan-a", 40 * mb, now.AddDays(-3));
var middle = Entry("b-middle", "orphan-b", 190 * mb, now.AddDays(-2));
var newest = Entry("c-newest", "orphan-c", 30 * mb, now.AddDays(-1));
var overCap = SessionHousekeeping.SelectDeletions(new[] { newest, older, middle }, live, now);
Expect(overCap.Contains("a-older") && overCap.Contains("b-middle") && !overCap.Contains("c-newest"), "200 MB is applied before the 30 day mark, oldest files first");

var liveHuge = Entry("live-huge", "live", 250 * mb, now.AddDays(-1));
var liveKept = SessionHousekeeping.SelectDeletions(new[] { liveHuge, youngOrphan }, live, now);
Expect(!liveKept.Contains("live-huge") && liveKept.Contains("young-orphan"), "an open tab is not deleted to satisfy the cap");

var oldBig = Entry("old-big", "orphan-old-big", 180 * mb, now.AddDays(-31));
var youngSmall = Entry("young-small", "orphan-young-small", 30 * mb, now.AddDays(-2));
var ageFirst = SessionHousekeeping.SelectDeletions(new[] { oldBig, youngSmall }, live, now);
Expect(ageFirst.Count == 1 && ageFirst[0] == "old-big", "dropping expired files can bring the folder under 200 MB before newer files are touched");

Console.WriteLine("session housekeeping checks passed");

static SessionHousekeeping.FileEntry Entry(string path, string tabId, long length, DateTime written)
{
    return new SessionHousekeeping.FileEntry(path, tabId, length, written);
}

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
