using Microsoft.Extensions.AI;

namespace Rag.Core.Ai;

/// <summary>
/// Turns text into embedding vectors.
///
/// Qwen3-Embedding is "instruction aware": QUESTIONS should be prefixed with a short task
/// description, DOCUMENTS should not. Getting this asymmetry right noticeably improves
/// retrieval, and the original course never mentions it.
/// </summary>
public sealed class EmbeddingService(IEmbeddingGenerator<string, Embedding<float>> generator)
{
    private const string QueryInstruction =
        "Instruct: Given a question, retrieve passages that answer the question\nQuery: ";

    /// <summary>Embed a user question (with the instruction prefix).</summary>
    public async Task<float[]> EmbedQueryAsync(string question, CancellationToken ct = default)
    {
        var result = await generator.GenerateAsync([QueryInstruction + question], cancellationToken: ct);
        return result[0].Vector.ToArray();
    }

    /// <summary>Embed document sentences (no prefix), in batches to keep memory use predictable.</summary>
    public async Task<List<float[]>> EmbedDocumentsAsync(
        IReadOnlyList<string> texts, int batchSize = 32, CancellationToken ct = default)
    {
        var vectors = new List<float[]>(texts.Count);
        for (int i = 0; i < texts.Count; i += batchSize)
        {
            var batch = texts.Skip(i).Take(batchSize).ToList();
            var result = await generator.GenerateAsync(batch, cancellationToken: ct);
            vectors.AddRange(result.Select(e => e.Vector.ToArray()));
        }
        return vectors;
    }

    /// <summary>Embed text as-is (used by the Phase 1 similarity demo).</summary>
    public async Task<float[]> EmbedRawAsync(string text, CancellationToken ct = default)
    {
        var result = await generator.GenerateAsync([text], cancellationToken: ct);
        return result[0].Vector.ToArray();
    }
}
