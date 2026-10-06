using System.Text.Json;

namespace RockfaxDesk;

/// <summary>A place the user navigated to: a UKC route (Kind 0), a crag (Kind 1) or a
/// plain tab view (Kind 2 — UkcId then carries the tab index). Route entries carry the
/// full summary when it was known, so back/forward restores grade/tech/star chips and
/// the "open crag" link instead of a bare id; map-tab entries carry the map view.</summary>
internal sealed record NavEntry(int Kind, int UkcId, string Name, RouteSummary? Route = null,
                                int Tab = -1, float MapZoom = 0, float MapLat = 0, float MapLng = 0)
{
    public const int KindRoute = 0;
    public const int KindCrag = 1;
    public const int KindTab = 2;

    public bool HasMapView => MapZoom > 0;

    /// <summary>The summary to open this entry with — the stored payload when present.</summary>
    public RouteSummary RouteOrFallback() => Route ?? new RouteSummary(Name, "", "", 0, "", UkcId, 0, 0);
}

/// <summary>Everything worth keeping between sessions: recent routes and crags.
/// Stored on this device only — nothing about browsing habits leaves the machine.</summary>
internal sealed record SessionState(List<NavEntry> RecentRoutes, List<NavEntry> RecentCrags)
{
    public static SessionState Empty { get; } = new([], []);
}

/// <summary>Loads/saves the session file and owns the pure recent-list behaviour.</summary>
internal static class SessionStore
{
    public const int RecentCap = 20;

    public static string PathFor()
        => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RockfaxDesk", "session.json");

    /// <summary>Moves the entry to the front of the list, deduplicating by (kind, id).</summary>
    public static void PushRecent(List<NavEntry> recents, NavEntry entry, int cap = RecentCap)
    {
        recents.RemoveAll(r => r.Kind == entry.Kind && r.UkcId == entry.UkcId);
        recents.Insert(0, entry);
        if (recents.Count > cap) recents.RemoveRange(cap, recents.Count - cap);
    }

    public static SessionState Load()
    {
        try
        {
            string path = PathFor();
            if (!File.Exists(path)) return SessionState.Empty;
            SessionState? state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(path));
            return state ?? SessionState.Empty;
        }
        catch
        {
            return SessionState.Empty; // a broken session file is worse than no session
        }
    }

    public static void Save(SessionState state)
    {
        try
        {
            string path = PathFor();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(state));
        }
        catch
        {
            // persistence is best-effort; the app works fine without it
        }
    }
}
