using Rag.Core.Retrieval;
using Rag.Core.Text;

/// <summary>
/// Offline checks for the pure logic (no Ollama, no database needed).
/// Run with:  dotnet run --project src/Rag.Cli -- selftest
/// </summary>
public static class SelfTest
{
    private static int _failures;

    public static int Run()
    {
        SentenceSplitting();
        CosineMaths();
        WindowMerging();
        Fusion();

        Console.WriteLine(_failures == 0 ? "\nAll checks passed." : $"\n{_failures} check(s) FAILED.");
        return _failures == 0 ? 0 : 1;
    }

    private static void SentenceSplitting()
    {
        Console.WriteLine("Sentence splitting");
        var s = SentenceSplitter.Split(
            "Dr. Smith moved to the U.S. in 1990. He earned $3.5 million! Was it worth it? " +
            "J. R. R. Tolkien wrote books.\n\n== History ==\nThe treaty was signed in 1948.");
        Check(s.Count == 5, $"5 sentences expected, got {s.Count}: {string.Join(" | ", s)}");
        Check(s[0] == "Dr. Smith moved to the U.S. in 1990.", $"abbreviations kept together: '{s[0]}'");
        Check(s[3].StartsWith("J. R. R. Tolkien"), "initials kept together");
        Check(!s.Any(x => x.Contains("==")), "headings dropped");

        var wrapped = SentenceSplitter.Split("This sentence was hard\nwrapped across two lines. The next sentence is here.");
        var legal = SentenceSplitter.Split("The court ruled in Schalk and Kopf v. Austria that marriage was not required. The judgment was delivered in 2010.");
        Check(legal.Count == 2, $"'v.' in case names is not a sentence end, got {legal.Count}");

        Check(wrapped.Count == 2 && wrapped[0] == "This sentence was hard wrapped across two lines.", "wrapped lines re-joined");
    }

    private static void CosineMaths()
    {
        Console.WriteLine("Cosine similarity");
        Check(Math.Abs(VectorMath.CosineSimilarity([1, 0], [1, 0]) - 1) < 1e-9, "same direction = 1");
        Check(Math.Abs(VectorMath.CosineSimilarity([1, 0], [0, 1])) < 1e-9, "perpendicular = 0");
        Check(Math.Abs(VectorMath.CosineSimilarity([1, 0], [-1, 0]) + 1) < 1e-9, "opposite = -1");
        Check(Math.Abs(VectorMath.CosineSimilarity([1, 2, 3], [2, 4, 6]) - 1) < 1e-9, "length does not matter, only direction");
    }

    private static void WindowMerging()
    {
        Console.WriteLine("Context window merging");
        static SearchHit Hit(string file, int n) => new(n, file, n, $"s{n}", 0);

        // The course's own example: hits at 4 and 8, window +/-2 -> [2..6] and [6..10] share sentence 6.
        var w = WindowMerger.SelectWindows([Hit("a.txt", 4), Hit("a.txt", 8)], windowSize: 2, maxWindows: 5);
        Check(w.Count == 1 && w[0].Start == 2 && w[0].End == 10, $"4 and 8 merge into one window [2..10], got {string.Join(", ", w)}");

        // Touching windows ([2..6] and [7..11]) are merged too, so no gap and no duplicate.
        w = WindowMerger.SelectWindows([Hit("a.txt", 4), Hit("a.txt", 9)], 2, 5);
        Check(w.Count == 1, "adjacent windows merge");

        // Far apart = separate windows.
        w = WindowMerger.SelectWindows([Hit("a.txt", 4), Hit("a.txt", 20)], 2, 5);
        Check(w.Count == 2, "distant hits stay separate");

        // Same sentence numbers in DIFFERENT files never merge.
        w = WindowMerger.SelectWindows([Hit("a.txt", 4), Hit("b.txt", 4)], 2, 5);
        Check(w.Count == 2, "different files never merge");

        // A later hit can bridge two existing windows.
        w = WindowMerger.SelectWindows([Hit("a.txt", 0), Hit("a.txt", 12), Hit("a.txt", 6)], 3, 5);
        Check(w.Count == 1 && w[0].Start == 0 && w[0].End == 15, $"bridging hit merges all three, got {string.Join(", ", w)}");

        // Stops at maxWindows distinct windows; window start never goes below 0.
        var many = Enumerable.Range(0, 10).Select(i => Hit($"f{i}.txt", 1));
        w = WindowMerger.SelectWindows(many, 3, 5);
        Check(w.Count == 5 && w.All(x => x.Start == 0), "max 5 windows, start clamped at 0");
    }

    private static void Fusion()
    {
        Console.WriteLine("Reciprocal rank fusion");
        static SearchHit Hit(long id) => new(id, "f.txt", (int)id, "", 0);
        var vector = new List<SearchHit> { Hit(1), Hit(2), Hit(3) };
        var keyword = new List<SearchHit> { Hit(3), Hit(4), Hit(1) };
        var fused = RankFusion.Fuse([vector, keyword]);
        Check(fused[0].Id == 1 && fused[1].Id == 3, $"items high in both lists win, got {string.Join(",", fused.Select(f => f.Id))}");
        Check(fused.Count == 4, "union of both lists");
    }

    private static void Check(bool ok, string description)
    {
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {description}");
        if (!ok) _failures++;
    }
}
