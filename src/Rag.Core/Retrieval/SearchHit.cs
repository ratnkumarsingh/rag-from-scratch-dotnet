namespace Rag.Core.Retrieval;

/// <summary>
/// One matching sentence from a search. (The book uses this record but never prints it.)
/// Score = cosine distance for vector search (smaller = closer), ts_rank for keyword search,
/// or the fused RRF score for hybrid search (larger = better).
/// </summary>
public sealed record SearchHit(long Id, string FileName, int SentenceNumber, string Content, double Score);
