using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Rag.Core.Retrieval;

namespace Rag.Core.Generation;

/// <summary>
/// Phase 5: a second, smarter sorting pass. Vector search is fast but rough; here the LLM
/// reads each candidate window and scores how well it answers the question (0-10).
///
/// This is slow on a CPU (one LLM call per window), so it is optional. Dedicated
/// "cross-encoder" re-ranking models do the same job much faster when you have a GPU.
/// Uses the BASE model: the RAG system prompt in rag-granite would interfere with scoring.
/// </summary>
public sealed partial class LlmReranker(IChatClient baseChat)
{
    public async Task<List<ContextWindow>> RerankAsync(string question, List<ContextWindow> windows, CancellationToken ct = default)
    {
        var scored = new List<(ContextWindow Window, double Score)>();
        foreach (var w in windows)
        {
            var prompt = $"""
                Rate how useful the passage is for answering the question.
                Reply with a single number from 0 (useless) to 10 (fully answers it). No other text.

                Question: {question}

                Passage: {string.Join(" ", w.Sentences)}
                """;

            var response = await baseChat.GetResponseAsync(prompt, new ChatOptions { Temperature = 0 }, ct);
            scored.Add((w, ParseScore(response.Text)));
        }

        // Stable sort: equal scores keep their original (vector) order.
        return scored.OrderByDescending(s => s.Score).Select(s => s.Window).ToList();
    }

    internal static double ParseScore(string text)
    {
        var m = FirstNumber().Match(text);
        return m.Success ? double.Parse(m.Value, CultureInfo.InvariantCulture) : 0;
    }

    [GeneratedRegex(@"\d+(\.\d+)?")]
    private static partial Regex FirstNumber();
}
