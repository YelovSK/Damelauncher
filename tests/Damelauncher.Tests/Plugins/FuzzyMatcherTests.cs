using Damelauncher.Plugins;

namespace Damelauncher.Tests.Plugins;

[TestClass]
public sealed class FuzzyMatcherTests
{
    [TestMethod]
    public void TheQuerysLettersHaveToAppearInOrder()
    {
        Assert.IsNotNull(FuzzyMatcher.Score("ntpd", "Notepad"));
        Assert.IsNull(FuzzyMatcher.Score("dpn", "Notepad"));
        Assert.IsNull(FuzzyMatcher.Score("xyz", "Notepad"));
    }

    [TestMethod]
    public void CaseDoesntMatter() =>
        Assert.AreEqual(FuzzyMatcher.Score("notepad", "Notepad"), FuzzyMatcher.Score("NOTEPAD", "Notepad"));

    [TestMethod]
    public void AnEmptyQueryOrOneLongerThanTheNameMatchesNothing()
    {
        Assert.IsNull(FuzzyMatcher.Score(string.Empty, "Notepad"));
        Assert.IsNull(FuzzyMatcher.Score("notepad++", "Notepad"));
    }

    [TestMethod]
    public void ANameStartingWithTheQueryRanksAboveOneWithItLater() =>
        Assert.IsGreaterThan(FuzzyMatcher.Score("note", "Sticky Notes")!.Value, FuzzyMatcher.Score("note", "Notepad")!.Value);

    [TestMethod]
    public void WordInitialsRankAboveTheSameLettersInsideAWord() =>
        Assert.IsGreaterThan(FuzzyMatcher.Score("vsc", "Devscope")!.Value, FuzzyMatcher.Score("vsc", "Visual Studio Code")!.Value);

    // The "C" of "Command" comes first, but "Code" is where the query really is.
    [TestMethod]
    public void AnEarlierPartialMatchDoesntSpoilALaterWholeOne() =>
        Assert.AreEqual(FuzzyMatcher.Score("code", "Example Code"), FuzzyMatcher.Score("code", "Command Code"));

    [TestMethod]
    public void OfTwoEquallyGoodMatchesTheShorterNameRanksHigher() =>
        Assert.IsGreaterThan(FuzzyMatcher.Score("paint", "Paint 3D")!.Value, FuzzyMatcher.Score("paint", "Paint")!.Value);

    // Other adjustments to a score, like the launch history's boost, are measured in points, so
    // the length tiebreak must stay below one.
    [TestMethod]
    public void TheShorterNameTiebreakIsWorthLessThanAPoint() =>
        Assert.IsLessThan(
            FuzzyMatcher.Point,
            FuzzyMatcher.Score("a", "A")!.Value - FuzzyMatcher.Score("a", "A" + new string('x', 500))!.Value);
}
