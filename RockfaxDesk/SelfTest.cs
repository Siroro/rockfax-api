using System.Text.Json;
using RockfaxApi;

namespace RockfaxDesk;

/// <summary>
/// Headless end-to-end check of the desktop app's data paths against the live API:
/// `dotnet run --project RockfaxDesk -- --self-test`. Exits 0 when every step passes.
/// </summary>
internal static class SelfTest
{
    public static async Task<int> RunAsync()
    {
        int failures = 0;

        void Check(string name, bool passed, string detail)
        {
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")}  {name}  ({detail})");
            if (!passed) failures++;
        }

        using var form = new MainForm();
        // Constructing WinForms controls installs the WindowsFormsSynchronizationContext,
        // whose callbacks need a message pump we don't run headless — drop it so awaits
        // in the test hooks continue on the thread pool instead of hanging.
        SynchronizationContext.SetSynchronizationContext(null);

        try
        {
            int freeCragCount = await form.LoadFreeCragsForTestAsync();
            Check("free-crags", freeCragCount > 0, $"{freeCragCount} crags listed");
        }
        catch (Exception ex)
        {
            Check("free-crags", false, ex.Message);
        }

        try
        {
            int markerCount = await form.LoadMarkersForTestAsync();
            Check("crag-markers", markerCount > 0, $"{markerCount} markers");
        }
        catch (Exception ex)
        {
            Check("crag-markers", false, ex.Message);
        }

        try
        {
            (int resultCount, string firstName) = await form.SearchForTestAsync("Flying Buttress");
            Check("route-search", resultCount > 0, $"{resultCount} routes, first: {firstName}");
        }
        catch (Exception ex)
        {
            Check("route-search", false, ex.Message);
        }

        try
        {
            (int height, int pitches) = await form.RouteInfoForTestAsync(3212);
            Check("route-info", height > 0, $"height {height}m, {pitches} pitches");
        }
        catch (Exception ex)
        {
            Check("route-info", false, ex.Message);
        }

        Console.WriteLine(failures == 0 ? "SELF-TEST PASSED" : $"SELF-TEST FAILED ({failures} failures)");
        return failures == 0 ? 0 : 1;
    }
}
