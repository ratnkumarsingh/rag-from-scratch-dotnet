using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Rag.Core.Ai;
using Rag.Core.Data;

namespace Rag.Core.Retrieval;

/// <summary>
/// Finds the text that is relevant to a question.
/// Phase 3: plain vector search.  Phase 4: windows + context.  Phase 5: hybrid search.
/// </summary>
public sealed class Retriever(RagDbContext db, EmbeddingService embeddings, RagSettings settings)
{
    // ---------------------------------------------------------------- Phase 3

    /// <summary>
    /// Exact top-k vector search. EF Core translates CosineDistance() into pgvector's
    /// "&lt;=&gt;" operator:  SELECT ... ORDER BY embedding &lt;=&gt; @query LIMIT @k
    /// </summary>
    public async Task<List<SearchHit>> VectorSearchAsync(float[] queryVector, int k, CancellationToken ct = default)
    {
        await EnsureSessionSettingsAsync(ct);
        var q = new Vector(queryVector);
        return await db.TextEmbeddings
            .AsNoTracking()
            .OrderBy(e => e.Embedding.CosineDistance(q))
            .Take(k)
            .Select(e => new SearchHit(e.Id, e.FileName, e.SentenceNumber, e.Content, e.Embedding.CosineDistance(q)))
            .ToListAsync(ct);
    }

    private bool _sessionReady;

    /// <summary>
    /// Once you add an HNSW index (Phase 5), pgvector returns at most hnsw.ef_search rows
    /// (default 40) per query, fewer than our 50 candidates. Raising it per session avoids that.
    /// We open the connection ourselves so EF Core keeps using the same one (and the setting sticks).
    /// </summary>
    private async Task EnsureSessionSettingsAsync(CancellationToken ct)
    {
        if (_sessionReady) return;
        await db.Database.OpenConnectionAsync(ct);
        int efSearch = Math.Max(100, settings.CandidatePool * 2);   // a plain number, so no SQL-injection risk
        await db.Database.ExecuteSqlRawAsync("SET hnsw.ef_search = " + efSearch, ct);
        _sessionReady = true;
    }

    // ---------------------------------------------------------------- Phase 5

    /// <summary>
    /// Keyword (full-text) search with PostgreSQL's built-in text search.
    /// Great at exact names and rare terms, which embeddings sometimes miss.
    /// The interpolated values become SQL parameters, so this is safe from SQL injection.
    ///
    /// Deviation from the book: websearch_to_tsquery joins every word with AND, so a sentence must
    /// contain ALL the question's words. A natural question almost never fits in one sentence
    /// (22 of our 24 eval questions matched nothing), which silently turned hybrid into plain
    /// vector search. We switch the ANDs to ORs and let ts_rank put sentences matching more words first.
    /// </summary>
    public async Task<List<SearchHit>> KeywordSearchAsync(string question, int k, CancellationToken ct = default)
    {
        var rows = await db.Database.SqlQuery<KeywordRow>($"""
            WITH q AS (
                SELECT replace(websearch_to_tsquery('english', {question})::text, ' & ', ' | ')::tsquery AS query
            )
            SELECT id AS "Id", file_name AS "FileName", sentence_number AS "SentenceNumber", content AS "Content",
                   ts_rank(to_tsvector('english', content), q.query) AS "Rank"
            FROM text_embeddings, q
            WHERE to_tsvector('english', content) @@ q.query
            ORDER BY "Rank" DESC
            LIMIT {k}
            """).ToListAsync(ct);

        return rows.Select(r => new SearchHit(r.Id, r.FileName, r.SentenceNumber, r.Content, r.Rank)).ToList();
    }

    /// <summary>Hybrid search: run vector + keyword search and fuse the two rankings with RRF.</summary>
    public async Task<List<SearchHit>> HybridSearchAsync(string question, float[] queryVector, int k, CancellationToken ct = default)
    {
        var vector = await VectorSearchAsync(queryVector, k, ct);
        var keyword = await KeywordSearchAsync(question, k, ct);
        return RankFusion.Fuse([vector, keyword]).Take(k).ToList();
    }

    // ---------------------------------------------------------------- Phase 4

    /// <summary>
    /// The full retrieval pipeline:
    /// question -> embedding -> many candidate hits -> merged, non-overlapping windows -> sentence text.
    /// </summary>
    public async Task<List<ContextWindow>> RetrieveWindowsAsync(
        string question, bool hybrid = false, int? maxWindows = null, CancellationToken ct = default)
    {
        var queryVector = await embeddings.EmbedQueryAsync(question, ct);

        var candidates = hybrid
            ? await HybridSearchAsync(question, queryVector, settings.CandidatePool, ct)
            : await VectorSearchAsync(queryVector, settings.CandidatePool, ct);

        var windows = WindowMerger.SelectWindows(candidates, settings.WindowSize, maxWindows ?? settings.MaxWindows);
        await LoadSentencesAsync(windows, ct);
        return windows;
    }

    /// <summary>Fetches the actual sentences for each window (one small indexed query per window).</summary>
    public async Task LoadSentencesAsync(IEnumerable<ContextWindow> windows, CancellationToken ct = default)
    {
        foreach (var w in windows)
        {
            var sentences = await db.TextEmbeddings
                .AsNoTracking()
                .Where(e => e.FileName == w.FileName && e.SentenceNumber >= w.Start && e.SentenceNumber <= w.End)
                .OrderBy(e => e.SentenceNumber)
                .Select(e => e.Content)
                .ToListAsync(ct);

            w.Sentences.Clear();
            w.Sentences.AddRange(sentences);
        }
    }
}

/// <summary>Shape of a keyword-search row (the SQL column aliases must match these property names).</summary>
internal sealed class KeywordRow
{
    public long Id { get; set; }
    public string FileName { get; set; } = "";
    public int SentenceNumber { get; set; }
    public string Content { get; set; } = "";
    public float Rank { get; set; }
}
