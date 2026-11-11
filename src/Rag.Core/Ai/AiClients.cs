using Microsoft.Extensions.AI;
using OllamaSharp;

namespace Rag.Core.Ai;

/// <summary>
/// Creates the two AI "clients" the whole system needs.
///
/// OllamaApiClient implements Microsoft.Extensions.AI's standard interfaces:
///   IChatClient                                    -> talk to an LLM
///   IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt; -> turn text into vectors
/// Because the rest of the code only sees these interfaces, you could later swap Ollama
/// for another provider without touching the RAG logic.
/// </summary>
public static class AiClients
{
    // Local models on a laptop CPU can be slow: allow up to 10 minutes per request.
    private static HttpClient Http(RagSettings s) =>
        new() { BaseAddress = new Uri(s.OllamaUrl), Timeout = TimeSpan.FromMinutes(10) };

    public static IChatClient CreateChatClient(RagSettings s, string? model = null) =>
        new OllamaApiClient(Http(s), model ?? s.ChatModel);

    public static IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(RagSettings s) =>
        new OllamaApiClient(Http(s), s.EmbeddingModel);
}
