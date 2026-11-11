using System.Text.Json;
using Rag.Core.Generation;

namespace Rag.Core.Evaluation;

/// <summary>One test question and the file that contains its answer.</summary>
public sealed record EvalQuestion(string Question, string ExpectedFile);

/// <summary>
/// Phase 5: measures retrieval quality so you can tell whether a change actually helped.
///   Hit rate@k: share of questions where the right file is among the k windows.
///   MRR (mean reciprocal rank): rewards putting the right file FIRST (1st = 1, 2nd = 0.5, 3rd = 0.33...).
/// </summary>
public sealed class Evaluator(RagAnswerer rag)
{
    public static List<EvalQuestion> Load(string path) =>
        JsonSerializer.Deserialize<List<EvalQuestion>>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

    public async Task<(double HitRate, double Mrr)> RunAsync(IReadOnlyList<EvalQuestion> questions, RetrievalMode mode, CancellationToken ct = default)
    {
        int hits = 0;
        double reciprocalRankSum = 0;

        Console.WriteLine($"Mode: {mode}");
        foreach (var q in questions)
        {
            var context = await rag.RetrieveAsync(q.Question, mode, ct);
            int rank = context.Windows.FindIndex(w => w.FileName.Equals(q.ExpectedFile, StringComparison.OrdinalIgnoreCase)) + 1;

            if (rank > 0) { hits++; reciprocalRankSum += 1.0 / rank; }
            Console.WriteLine($"  {(rank > 0 ? $"rank {rank}" : "MISS"),-7} {q.Question}");
        }

        double hitRate = questions.Count == 0 ? 0 : (double)hits / questions.Count;
        double mrr = questions.Count == 0 ? 0 : reciprocalRankSum / questions.Count;
        Console.WriteLine($"Hit rate: {hitRate:P0}   MRR: {mrr:F2}   ({questions.Count} questions)");
        return (hitRate, mrr);
    }
}
