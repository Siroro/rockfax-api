using RockfaxDesk;
using Xunit;

namespace RockfaxApi.Tests;

public class NavHistoryTests
{
    private static NavEntry Route(int id, string name = "route", string grade = "HVS 5a") =>
        new(NavEntry.KindRoute, id, name, new RouteSummary(name, grade, "5a", 2, "Some Crag", id, 0, 77));

    private static NavEntry Crag(int id, string name = "crag") => new(NavEntry.KindCrag, id, name);

    [Fact]
    public void Back_returns_the_previous_page_and_can_round_trip()
    {
        var nav = new NavHistory();
        nav.Push(Route(1, "A"));
        nav.Push(Crag(2, "B"));
        Assert.True(nav.CanBack);

        NavEntry back = nav.Back()!;
        Assert.Equal(1, back.UkcId);
        Assert.Equal(NavEntry.KindRoute, back.Kind);
        Assert.True(nav.CanForward);

        NavEntry forward = nav.Forward()!;
        Assert.Equal(2, forward.UkcId);
        Assert.False(nav.CanForward);
    }

    [Fact]
    public void Back_keeps_the_full_route_payload_so_the_page_restores_completely()
    {
        var nav = new NavHistory();
        nav.Push(Route(52150, "Aquarius", "E2 5c"));
        nav.Push(Crag(104, "Stanage Popular"));

        NavEntry back = nav.Back()!;
        Assert.NotNull(back.Route);
        Assert.Equal("E2 5c", back.Route!.Grade);
        Assert.Equal("Some Crag", back.Route.CragName);
        Assert.Equal(77, back.Route.CragUkcId); // "open crag" survives the round trip
        Assert.Equal("E2 5c", back.RouteOrFallback().Grade);
    }

    [Fact]
    public void RouteOrFallback_builds_a_bare_summary_when_no_payload_was_stored()
    {
        NavEntry old = new(NavEntry.KindRoute, 9, "Ancient"); // e.g. from an older session.json
        Assert.Null(old.Route);
        RouteSummary fallback = old.RouteOrFallback();
        Assert.Equal(9, fallback.UkcId);
        Assert.Equal("", fallback.Grade);
    }

    [Fact]
    public void Same_page_reopens_refresh_the_payload_without_pushing_a_duplicate()
    {
        var nav = new NavHistory();
        nav.Push(Crag(2, "B"));
        nav.Push(Route(1, "A", "VS 4c"));
        nav.Push(Route(1, "A", "HVS 5a")); // same id: refresh, not a new entry

        Assert.Single(nav.BackStack);
        Assert.Equal("HVS 5a", nav.Current!.Route!.Grade); // richer payload adopted

        NavEntry back = nav.Back()!;
        Assert.Equal(2, back.UkcId); // lands on the crag, not on A twice
        Assert.False(nav.CanBack);
    }

    [Fact]
    public void A_fresh_open_clears_the_forward_stack()
    {
        var nav = new NavHistory();
        nav.Push(Route(1, "A"));
        nav.Push(Route(2, "B"));
        nav.Back(); // now on A, forward holds B
        nav.Push(Crag(3, "C"));

        Assert.False(nav.CanForward);
        Assert.Single(nav.BackStack); // the back() emptied it; opening C pushed only A
        Assert.Equal(1, nav.BackStack[0].UkcId);
    }

    [Fact]
    public void Back_at_the_bottom_returns_null_and_keeps_state()
    {
        var nav = new NavHistory();
        nav.Push(Route(1, "A"));
        nav.Back();
        Assert.False(nav.CanBack);
        Assert.Null(nav.Back());
        Assert.Equal(1, nav.Current!.UkcId); // still on A
    }

    [Fact]
    public void Deep_histories_are_capped()
    {
        var nav = new NavHistory();
        for (int i = 0; i < 120; i++) nav.Push(Route(i, $"r{i}"));
        Assert.Equal(100, nav.BackStack.Count);
        Assert.Equal(119, nav.Current!.UkcId);
    }

    [Fact]
    public void Route_payload_survives_the_session_json_round_trip()
    {
        var entry = Route(52150, "Aquarius");
        var state = new SessionState([entry], [Crag(104, "Stanage")]);
        string json = System.Text.Json.JsonSerializer.Serialize(state);
        SessionState? back = System.Text.Json.JsonSerializer.Deserialize<SessionState>(json);

        Assert.NotNull(back);
        NavEntry restored = back!.RecentRoutes[0];
        Assert.Equal("Aquarius", restored.Name);
        Assert.NotNull(restored.Route);
        Assert.Equal("HVS 5a", restored.Route!.Grade);
        Assert.Equal(2, restored.Route.Stars);
    }

    [Fact]
    public void Older_session_files_without_a_payload_still_load()
    {
        const string oldJson = """{"RecentRoutes":[{"Kind":0,"UkcId":42,"Name":"Legacy"}],"RecentCrags":[]}""";
        SessionState? state = System.Text.Json.JsonSerializer.Deserialize<SessionState>(oldJson);
        Assert.NotNull(state);
        NavEntry legacy = state!.RecentRoutes[0];
        Assert.Equal(42, legacy.UkcId);
        Assert.Null(legacy.Route);
    }
}
