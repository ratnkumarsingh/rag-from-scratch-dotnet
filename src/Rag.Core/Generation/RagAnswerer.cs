using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Rag.Core.Retrieval;

namespace Rag.Core.Generation;

/// <summary>Everything retrieval produced for one question.</summary>
public sealed record RagContext(string Question, List<ContextWindow> Windows, string Prompt)
{
    /// <summary>Numbered source list, matching the [Source n] labels in the prompt.</summary>
    public IEnumerable<string> Sources =>
        Windows.Select((w, i) => $"[Source {i + 1}] {PromptBuilder.SourceTitle(w.FileName)} (sentences {w.Start}-{w.End})");
}

public enum RetrievalMode { Vector, Hybrid, HybridRerank }

/// <summary>
/// The complete RAG pipeline: the C# version of the course's run_rag.py.
///   1. retrieve: question -> context windows
///   2. augment:  windows + question -> prompt
///   3. generate: prompt -> streamed answer from the LLM
/// </summary>
public sealed class RagAnswerer(Retriever retriever, IChatClient chat, RagSettings settings, LlmReranker? reranker = null)
{
    public async Task<RagContext> RetrieveAsync(string question, RetrievalMode mode = RetrievalMode.Vector, CancellationToken ct = default)
    {
        bool rerank = mode == RetrievalMode.HybridRerank && reranker is not null;

        // When re-ranking, fetch twice as many windows, let the LLM sort them, keep the best ones.
        var windows = await retriever.RetrieveWindowsAsync(
            question,
            hybrid: mode != RetrievalMode.Vector,
            maxWindows: rerank ? settings.MaxWindows * 2 : settings.MaxWindows,
            ct: ct);

        if (rerank)
            windows = (await reranker!.RerankAsync(question, windows, ct)).Take(settings.MaxWindows).ToList();

        return new RagContext(question, windows, PromptBuilder.BuildPrompt(question, windows));
    }

    /// <summary>Streams the answer token by token, so the user sees it appear immediately.</summary>
    public async IAsyncEnumerable<string> AnswerAsync(RagContext context, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Only the user message is sent: the system prompt + examples come from the Modelfile.
        await foreach (var update in chat.GetStreamingResponseAsync(context.Prompt, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(update.Text)) yield return update.Text;
        }
    }
}
