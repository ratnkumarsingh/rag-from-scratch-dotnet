namespace Rag.Core;

/// <summary>
/// All the knobs of the RAG system in one place.
/// Every value can be overridden with an environment variable, so you never
/// have to edit code to try a different model or database.
/// </summary>
public sealed record RagSettings
{
    // ---- Ollama -------------------------------------------------------------
    public string OllamaUrl { get; init; } = Env("RAG_OLLAMA_URL", "http://localhost:11434");

    /// <summary>The custom chat model we create from the Modelfile (Phase 2 / Phase 4).</summary>
    public string ChatModel { get; init; } = Env("RAG_CHAT_MODEL", "rag-granite");

    /// <summary>The plain base model, used where our RAG system prompt would get in the way (e.g. re-ranking).</summary>
    public string BaseChatModel { get; init; } = Env("RAG_BASE_MODEL", "granite4.1:3b");

    public string EmbeddingModel { get; init; } = Env("RAG_EMBED_MODEL", "qwen3-embedding:0.6b");

    /// <summary>Must match the embedding model: qwen3-embedding:0.6b = 1024, nomic-embed-text = 768.</summary>
    public int EmbeddingDimensions { get; init; } = int.Parse(Env("RAG_EMBED_DIMS", "1024"));

    // ---- PostgreSQL ---------------------------------------------------------
    public string ConnectionString { get; init; } = Env("RAG_PG",
        "Host=localhost;Port=5432;Database=text_embeddings;Username=postgres;Password=postgres");

    // ---- Files --------------------------------------------------------------
    /// <summary>Folder of .txt files that get ingested (Wikipedia articles + extracted documents).</summary>
    public string CorpusFolder { get; init; } = Env("RAG_CORPUS", Path.Combine("data", "all_articles"));

    // ---- Retrieval ----------------------------------------------------------
    /// <summary>Sentences taken before AND after each matching sentence (the course used 3).</summary>
    public int WindowSize { get; init; } = 3;

    /// <summary>How many distinct context windows we hand to the LLM (the course used 5).</summary>
    public int MaxWindows { get; init; } = 5;

    /// <summary>How many raw matches we fetch before merging overlaps (deliberately larger than MaxWindows).</summary>
    public int CandidatePool { get; init; } = 50;

    /// <summary>Embedding batch size during ingestion. Lower it if Ollama runs out of memory.</summary>
    public int EmbeddingBatchSize { get; init; } = 32;

    private static string Env(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;
}
