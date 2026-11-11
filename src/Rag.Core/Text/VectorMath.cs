namespace Rag.Core.Text;

/// <summary>
/// The maths behind similarity search, written out by hand so you can see it.
/// (In production pgvector does this inside the database.)
/// </summary>
public static class VectorMath
{
    /// <summary>
    /// Cosine similarity = cos(angle between a and b).
    ///  1.0 = same direction (same meaning), 0 = unrelated, -1 = opposite.
    /// </summary>
    public static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length) throw new ArgumentException("Vectors must have the same length.");
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA == 0 || normB == 0) return 0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    /// <summary>pgvector's "&lt;=&gt;" operator returns cosine DISTANCE = 1 - similarity (smaller = closer).</summary>
    public static double CosineDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b) => 1 - CosineSimilarity(a, b);
}
