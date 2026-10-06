using RockfaxDesk;
using RockfaxDesk.Controls;
using Xunit;

namespace RockfaxApi.Tests;

public class RouteSortTests
{
    private static RouteSummary R(string name, string grade = "", string tech = "", int stars = 0, int ukc = 1)
        => new(name, grade, tech, stars, "", ukc, 0, 0);

    [Fact]
    public void NamesSortCaseInsensitively()
    {
        Assert.True(CragView.CompareRoutes(R("abacus"), R("Zebra"), 0) < 0);
        Assert.True(CragView.CompareRoutes(R("Zebra"), R("abacus"), 0) > 0);
        Assert.Equal(0, CragView.CompareRoutes(R("Apollo"), R("apollo"), 0));
    }

    [Fact]
    public void GradesSortAdjectualThenEThenSport()
    {
        string[] ascending = { "M", "VD", "S", "HVS", "E1", "E2 5c", "E7", "4", "6a", "7c" };
        for (int i = 1; i < ascending.Length; i++)
        {
            Assert.True(CragView.CompareRoutes(R("a", ascending[i - 1]), R("b", ascending[i]), 1) < 0,
                $"{ascending[i - 1]} should sort before {ascending[i]}");
        }
    }

    [Fact]
    public void UnparseableGradesSortLast()
    {
        Assert.Equal(int.MaxValue, CragView.GradeKey("fridge"));
        Assert.True(CragView.CompareRoutes(R("a", "E9"), R("b", "fridge"), 1) < 0);
    }

    [Fact]
    public void EqualGradesBreakTieByName()
    {
        Assert.True(CragView.CompareRoutes(R("zebra", "VS 4c"), R("aardvark", "VS 4c"), 1) > 0);
        // Same adjectival grade, different suffix: the shorter grade sorts first.
        Assert.True(CragView.CompareRoutes(R("a", "VS"), R("b", "VS 4c"), 1) < 0);
    }

    [Fact]
    public void StarsCompareNumerically()
    {
        Assert.True(CragView.CompareRoutes(R("a", "", stars: 0), R("b", "", stars: 3), 3) < 0);
        Assert.Equal(0, CragView.CompareRoutes(R("a", "", stars: 2), R("a", "", stars: 2), 3));
    }

    [Fact]
    public void UkcIdsCompareNumerically()
    {
        // "999" > "1000" as strings — the numeric compare must win.
        Assert.True(CragView.CompareRoutes(R("a", ukc: 1000), R("b", ukc: 999), 4) > 0);
    }
}
