using Damelauncher.Plugins;
using Damelauncher.Search;

namespace Damelauncher.Tests.Search;

[TestClass]
public sealed class ResultSearchTests
{
    private readonly List<IReadOnlyList<LaunchResult>> _shown = [];
    private string _historyPath = null!;

    private IReadOnlyList<LaunchResult> LastShown => _shown[^1];

    [TestInitialize]
    public void CreateHistoryPath() =>
        _historyPath = Path.Combine(Path.GetTempPath(), $"damelauncher-history-{Guid.NewGuid():N}.txt");

    [TestCleanup]
    public void DeleteHistory() => File.Delete(_historyPath);

    [TestMethod]
    public void ShowsEveryProvidersResultsBestFirst()
    {
        var programs = new FakeProvider("programs");
        var settings = new FakeProvider("settings");
        programs.Answer = _ => [new(programs.Result("a"), 300), new(programs.Result("b"), 100)];
        settings.Answer = _ => [new(settings.Result("c"), 200)];
        using ResultSearch search = Create(programs, settings);

        search.Search("q");

        CollectionAssert.AreEqual(new[] { "a", "c", "b" }, Ids(LastShown));
    }

    [TestMethod]
    public void EqualScoresKeepTheProvidersOrderAndEachProvidersOwn()
    {
        var first = new FakeProvider("first");
        var second = new FakeProvider("second");
        first.Answer = _ => [new(first.Result("a"), 100), new(first.Result("b"), 100)];
        second.Answer = _ => [new(second.Result("c"), 100)];
        using ResultSearch search = Create(first, second);

        search.Search("q");

        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, Ids(LastShown));
    }

    [TestMethod]
    public void ShowsOnlyTheFiftyBestOfManyMatches()
    {
        var provider = new FakeProvider("many");
        // Scores 0 to 79 in a scrambled order, so the best ones arrive anywhere in the list.
        int[] scores = [.. Enumerable.Range(0, 80).Select(index => index * 37 % 80)];
        provider.Answer = _ => scores.Select(score => new ScoredResult(provider.Result($"{score}"), score));
        using ResultSearch search = Create(provider);

        search.Search("q");

        CollectionAssert.AreEqual(
            Enumerable.Range(30, 50).Reverse().Select(score => $"{score}").ToArray(),
            Ids(LastShown));
    }

    [TestMethod]
    public void ABlankQueryShowsNothingWithoutAskingTheProviders()
    {
        var provider = new FakeProvider("programs");
        using ResultSearch search = Create(provider);

        search.Search("   ");

        Assert.IsEmpty(LastShown);
        Assert.IsEmpty(provider.Queries);
    }

    [TestMethod]
    public void ProvidersGetTheQueryWithoutSurroundingSpaces()
    {
        var provider = new FakeProvider("programs");
        using ResultSearch search = Create(provider);

        search.Search("  note ");

        CollectionAssert.AreEqual(new[] { "note" }, provider.Queries);
    }

    [TestMethod]
    public void DisabledProvidersAreNotAsked()
    {
        var enabled = new FakeProvider("enabled");
        var disabled = new FakeProvider("disabled");
        enabled.Answer = _ => [new(enabled.Result("a"), 100)];
        disabled.Answer = _ => [new(disabled.Result("b"), 200)];
        using ResultSearch search = Create(enabled, disabled);
        search.SetRouting("disabled", ProviderRouting.Default with { Enabled = false, Keyword = "d" });

        search.Search("q");
        search.Search("d q");

        CollectionAssert.AreEqual(new[] { "a" }, Ids(LastShown));
        Assert.IsEmpty(disabled.Queries);
    }

    [TestMethod]
    public void AKeywordAndASpaceAskOnlyItsProviderAndLeaveTheKeywordOut()
    {
        var programs = new FakeProvider("programs");
        var files = new FakeProvider("files");
        using ResultSearch search = Create(programs, files);
        search.SetRouting("files", ProviderRouting.Default with { Keyword = "f" });

        search.Search("f  report  ");

        Assert.IsEmpty(programs.Queries);
        CollectionAssert.AreEqual(new[] { "report" }, files.Queries);
    }

    [TestMethod]
    public void KeywordsDontCareAboutCase()
    {
        var files = new FakeProvider("files");
        using ResultSearch search = Create(new FakeProvider("programs"), files);
        search.SetRouting("files", ProviderRouting.Default with { Keyword = "f" });

        search.Search("F report");

        CollectionAssert.AreEqual(new[] { "report" }, files.Queries);
    }

    // Otherwise a one-letter keyword would take over every query starting with that letter.
    [TestMethod]
    public void AKeywordWithoutASpaceAfterItIsAnOrdinaryQuery()
    {
        var programs = new FakeProvider("programs");
        var files = new FakeProvider("files");
        using ResultSearch search = Create(programs, files);
        search.SetRouting("files", ProviderRouting.Default with { Keyword = "f" });

        search.Search("f");
        search.Search("firefox");

        CollectionAssert.AreEqual(new[] { "f", "firefox" }, programs.Queries);
        CollectionAssert.AreEqual(new[] { "f", "firefox" }, files.Queries);
    }

    [TestMethod]
    public void AKeywordWithNothingAfterItYetShowsNothing()
    {
        var programs = new FakeProvider("programs");
        var files = new FakeProvider("files");
        using ResultSearch search = Create(programs, files);
        search.SetRouting("files", ProviderRouting.Default with { Keyword = "f" });

        search.Search("f ");

        Assert.IsEmpty(LastShown);
        Assert.IsEmpty(programs.Queries);
        Assert.IsEmpty(files.Queries);
    }

    [TestMethod]
    public void AProviderLeftOutOfNormalSearchesStillAnswersItsKeyword()
    {
        var programs = new FakeProvider("programs");
        var files = new FakeProvider("files");
        using ResultSearch search = Create(programs, files);
        search.SetRouting("files", new ProviderRouting(Enabled: true, Keyword: "f", InNormalSearches: false));

        search.Search("report");
        search.Search("f report");

        CollectionAssert.AreEqual(new[] { "report" }, programs.Queries);
        CollectionAssert.AreEqual(new[] { "report" }, files.Queries);
    }

    [TestMethod]
    public void AnUnknownKeywordIsPartOfAnOrdinaryQuery()
    {
        var programs = new FakeProvider("programs");
        using ResultSearch search = Create(programs);
        search.SetRouting("programs", ProviderRouting.Default with { Keyword = "p" });

        search.Search("visual studio");

        CollectionAssert.AreEqual(new[] { "visual studio" }, programs.Queries);
    }

    // Showing whichever provider answered first and then adding the rest made the list
    // flicker on every keystroke.
    [TestMethod]
    public void KeepsThePreviousResultsUntilEveryProviderHasAnswered()
    {
        var quick = new FakeProvider("quick");
        var slow = new FakeProvider("slow") { Answer = null };
        quick.Answer = _ => [new(quick.Result("a"), 100)];
        using ResultSearch search = Create(quick, slow);

        search.Search("q");
        Assert.IsEmpty(_shown);

        slow.Pending[0].SetResult([new(slow.Result("b"), 200)]);

        Assert.HasCount(1, _shown);
        CollectionAssert.AreEqual(new[] { "b", "a" }, Ids(LastShown));
    }

    [TestMethod]
    public void AnAnswerToAQueryThatWasTypedOverIsNeverShown()
    {
        var slow = new FakeProvider("slow") { Answer = null };
        using ResultSearch search = Create(slow);

        search.Search("n");
        search.Search("no");
        slow.Pending[0].SetResult([new(slow.Result("stale"), 100)]);

        Assert.IsEmpty(_shown);

        slow.Pending[1].SetResult([new(slow.Result("current"), 100)]);

        CollectionAssert.AreEqual(new[] { "current" }, Ids(LastShown));
    }

    [TestMethod]
    public void ShowsWhatsThereWhenAProviderTakesTooLongAndAddsItsAnswerLater()
    {
        var quick = new FakeProvider("quick");
        var slow = new FakeProvider("slow") { Answer = null };
        quick.Answer = _ => [new(quick.Result("a"), 100)];
        var partial = new TaskCompletionSource<IReadOnlyList<LaunchResult>>();
        using ResultSearch search = Create(quick, slow);
        search.ResultsChanged += results => partial.TrySetResult(results);

        search.Search("q");

        Assert.IsTrue(partial.Task.Wait(TimeSpan.FromSeconds(5)), "Nothing was shown after the deadline.");
        CollectionAssert.AreEqual(new[] { "a" }, Ids(partial.Task.Result));

        slow.Pending[0].SetResult([new(slow.Result("b"), 200)]);

        CollectionAssert.AreEqual(new[] { "b", "a" }, Ids(LastShown));
    }

    [TestMethod]
    public void AFailingProviderDoesntHideTheOthersResults()
    {
        var broken = new FakeProvider("broken") { Failure = new InvalidOperationException("Broken.") };
        var working = new FakeProvider("working");
        working.Answer = _ => [new(working.Result("a"), 100)];
        using ResultSearch search = Create(broken, working);

        search.Search("q");

        CollectionAssert.AreEqual(new[] { "a" }, Ids(LastShown));
    }

    // Providers refill the same list for every query, which mustn't reach what's already shown.
    [TestMethod]
    public void ShownResultsStayAsTheyWereWhenTheNextQueryIsAnswered()
    {
        var provider = new FakeProvider("programs");
        provider.Answer = query => [new(provider.Result(query), 100)];
        using ResultSearch search = Create(provider);

        search.Search("first");
        IReadOnlyList<LaunchResult> shownFirst = LastShown;
        search.Search("second");

        CollectionAssert.AreEqual(new[] { "first" }, Ids(shownFirst));
        CollectionAssert.AreEqual(new[] { "second" }, Ids(LastShown));
    }

    [TestMethod]
    public void ALaunchedResultRanksAboveAnEquallyGoodMatch()
    {
        var provider = new FakeProvider("programs");
        LaunchResult a = provider.Result("a");
        LaunchResult b = provider.Result("b");
        provider.Answer = _ => [new(a, 20 * FuzzyMatcher.Point), new(b, 20 * FuzzyMatcher.Point)];
        using ResultSearch search = Create(provider);

        search.RecordLaunch(b);
        search.Search("q");

        CollectionAssert.AreEqual(new[] { "b", "a" }, Ids(LastShown));
    }

    // Habit can lift a decent match over a slightly better one, but not a poor one over a good one.
    [TestMethod]
    public void LaunchingAPoorMatchOftenDoesntLiftItAboveAMuchBetterOne()
    {
        var provider = new FakeProvider("programs");
        LaunchResult good = provider.Result("good");
        LaunchResult poor = provider.Result("poor");
        provider.Answer = _ => [new(good, 40 * FuzzyMatcher.Point), new(poor, 10 * FuzzyMatcher.Point)];
        using ResultSearch search = Create(provider);

        for (int launch = 0; launch < 100; launch++)
        {
            search.RecordLaunch(poor);
        }

        search.Search("q");

        CollectionAssert.AreEqual(new[] { "good", "poor" }, Ids(LastShown));
    }

    [TestMethod]
    public void TheActionsMenuHasTheProvidersActionsAndForgettingOnlyOnceLaunched()
    {
        var provider = new FakeProvider("programs");
        LaunchResult result = provider.Result("a");
        provider.Actions.Add(provider.Result("Run as administrator"));
        using ResultSearch search = Create(provider);

        CollectionAssert.AreEqual(new[] { "Run as administrator" }, Titles(search.GetActions(result)));

        search.RecordLaunch(result);

        CollectionAssert.AreEqual(new[] { "Run as administrator", "Forget launches" }, Titles(search.GetActions(result)));
    }

    [TestMethod]
    public void ForgettingAResultsLaunchesUndoesItsBoost()
    {
        var provider = new FakeProvider("programs");
        LaunchResult a = provider.Result("a");
        LaunchResult b = provider.Result("b");
        provider.Answer = _ => [new(a, 100), new(b, 100)];
        using ResultSearch search = Create(provider);
        search.RecordLaunch(b);

        search.GetActions(b).Single(action => action.Title == "Forget launches").Execute();
        search.Search("q");

        CollectionAssert.AreEqual(new[] { "a", "b" }, Ids(LastShown));
    }

    private ResultSearch Create(params IResultProvider[] providers)
    {
        var search = new ResultSearch(UsageHistory.Load(_historyPath), providers);
        search.ResultsChanged += _shown.Add;
        return search;
    }

    private static string[] Ids(IReadOnlyList<LaunchResult> results) => [.. results.Select(result => result.Id)];

    private static string[] Titles(IReadOnlyList<LaunchResult> results) => [.. results.Select(result => result.Title)];
}
