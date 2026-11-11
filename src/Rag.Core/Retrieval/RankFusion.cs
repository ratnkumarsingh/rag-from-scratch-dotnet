namespace Rag.Core.Retrieval;

/// <summary>
/// Reciprocal Rank Fusion (RRF): combines several ranked lists into one (Phase 5, hybrid search).
/// score(item) = sum over lists of 1 / (k + rank), where rank starts at 1.
/// Items that rank high in BOTH lists float to the top. k = 60 is the usual default.
/// </summary>
public static class RankFusion
{
    public static List<SearchHit> Fuse(IEnumerable<IReadOnlyList<SearchHit>> rankedLists, int k = 60)
    {
        var scores = new Dictionary<long, double>();
        var byId = new Dictionary<long, SearchHit>();

        foreach (var list in rankedLists)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var hit = list[i];
                scores[hit.Id] = scores.GetValueOrDefault(hit.Id) + 1.0 / (k + i + 1);
                byId.TryAdd(hit.Id, hit);
            }
        }

        return scores
            .OrderByDescending(kv => kv.Value)
            .Select(kv => byId[kv.Key] with { Score = kv.Value })
            .ToList();
    }
}
