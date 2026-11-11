using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace Rag.Core.Data;

/// <summary>
/// EF Core gateway to PostgreSQL + pgvector.
/// </summary>
public class RagDbContext(RagSettings settings) : DbContext
{
    public DbSet<TextEmbedding> TextEmbeddings => Set<TextEmbedding>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        // UseVector() teaches Npgsql how to send/receive the pgvector "vector" type.
        options.UseNpgsql(settings.ConnectionString, npgsql => npgsql.UseVector());

    protected override void OnModelCreating(ModelBuilder model)
    {
        // CREATE EXTENSION IF NOT EXISTS vector;
        model.HasPostgresExtension("vector");

        model.Entity<TextEmbedding>(e =>
        {
            // Explicit snake_case names keep the raw SQL in Phase 5 simple and readable.
            e.ToTable("text_embeddings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FileName).HasColumnName("file_name").IsRequired();
            e.Property(x => x.SentenceNumber).HasColumnName("sentence_number");
            e.Property(x => x.Content).HasColumnName("content").IsRequired();

            // vector(1024): the size MUST match the embedding model's output size.
            e.Property(x => x.Embedding)
             .HasColumnName("embedding")
             .HasColumnType($"vector({settings.EmbeddingDimensions})");

            // Fast lookup of "sentences 10..16 of file X" when we build context windows.
            e.HasIndex(x => new { x.FileName, x.SentenceNumber });
        });
    }
}
