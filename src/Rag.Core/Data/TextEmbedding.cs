using Pgvector;

namespace Rag.Core.Data;

/// <summary>
/// One row in the vector database = one sentence from one file.
/// (This is the C# version of the course's SQLAlchemy TextEmbedding class.)
/// </summary>
public class TextEmbedding
{
    /// <summary>Primary key.</summary>
    public long Id { get; set; }

    /// <summary>Which .txt file the sentence came from, e.g. "Human_rights_in_Germany.txt".</summary>
    public string FileName { get; set; } = "";

    /// <summary>Position of the sentence inside its file: 0, 1, 2, ...  Used to fetch neighbours.</summary>
    public int SentenceNumber { get; set; }

    /// <summary>The sentence text itself.</summary>
    public string Content { get; set; } = "";

    /// <summary>The embedding: a list of numbers (1024 of them for qwen3-embedding:0.6b).</summary>
    public Vector Embedding { get; set; } = null!;
}
