namespace Rag.Core.Retrieval;

/// <summary>
/// A block of consecutive sentences from ONE file: sentences Start..End (inclusive).
/// </summary>
public sealed class ContextWindow
{
    public required string FileName { get; init; }
    public int Start { get; set; }
    public int End { get; set; }

    /// <summary>The search hits that produced this window, in rank order.</summary>
    public List<SearchHit> Hits { get; } = [];

    /// <summary>Filled in later with the actual sentence text.</summary>
    public List<string> Sentences { get; } = [];

    public override string ToString() => $"{FileName} [{Start}..{End}] ({Hits.Count} hit(s))";
}

/// <summary>
/// Turns a ranked list of matching sentences into a few non-overlapping context windows.
///
/// Each hit at sentence n wants the window [n - w, n + w].
/// Two windows in the same file overlap (or touch) when their ranges meet, i.e. when the hits
/// are at most 2w + 1 sentences apart. Overlapping windows are merged into one, so the LLM
/// never sees the same sentence twice. This fixes the course's off-by-one (it compared
/// the hits with w instead of 2w).
/// </summary>
public static class WindowMerger
{
    public static List<ContextWindow> SelectWindows(IEnumerable<SearchHit> rankedHits, int windowSize, int maxWindows)
    {
        var windows = new List<ContextWindow>();

        foreach (var hit in rankedHits)
        {
            int start = Math.Max(0, hit.SentenceNumber - windowSize);
            int end = hit.SentenceNumber + windowSize;

            // Every existing window of the same file that overlaps or touches [start, end].
            var overlapping = windows
                .Where(w => w.FileName == hit.FileName && start <= w.End + 1 && end >= w.Start - 1)
                .ToList();

            if (overlapping.Count == 0)
            {
                // A genuinely new region of text.
                if (windows.Count == maxWindows) break;   // we already have enough distinct windows
                var window = new ContextWindow { FileName = hit.FileName, Start = start, End = end };
                window.Hits.Add(hit);
                windows.Add(window);
                continue;
            }

            // Merge the new range (and any windows it bridges) into the first, best-ranked window.
            var keep = overlapping[0];
            keep.Start = Math.Min(keep.Start, start);
            keep.End = Math.Max(keep.End, end);
            keep.Hits.Add(hit);
            foreach (var other in overlapping.Skip(1))
            {
                keep.Start = Math.Min(keep.Start, other.Start);
                keep.End = Math.Max(keep.End, other.End);
                keep.Hits.AddRange(other.Hits);
                windows.Remove(other);
            }
        }

        return windows;
    }
}
