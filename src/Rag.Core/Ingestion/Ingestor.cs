using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Rag.Core.Ai;
using Rag.Core.Data;
using Rag.Core.Text;

namespace Rag.Core.Ingestion;

/// <summary>
/// Fills the vector database: the C# version of the course's populate_vector_db.py.
/// For every .txt file: split into sentences -> embed each sentence -> store (file, position, text, vector).
///
/// Improvements over the course version:
///  * Resumable: files already in the database are skipped, so a crash or Ctrl+C costs nothing.
///  * No duplicates: re-running does not insert the same sentences twice (use --reset to start over).
///  * file_name and sentence_number are always stored (the course's video forgot them).
/// </summary>
public sealed class Ingestor(RagDbContext db, EmbeddingService embeddings, RagSettings settings)
{
    public async Task IngestFolderAsync(string folder, bool reset = false, int? maxFiles = null, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);   // creates the extension + table on first run

        if (reset)
        {
            await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE text_embeddings RESTART IDENTITY;", ct);
            Console.WriteLine("Table emptied (--reset).");
        }

        var alreadyDone = (await db.TextEmbeddings.Select(e => e.FileName).Distinct().ToListAsync(ct)).ToHashSet();

        var files = Directory.EnumerateFiles(folder, "*.txt").OrderBy(f => f).ToList();
        if (maxFiles is int max) files = files.Take(max).ToList();
        Console.WriteLine($"{files.Count} file(s) in '{folder}', {alreadyDone.Count} already ingested.");

        var total = Stopwatch.StartNew();
        int fileNo = 0, sentenceCount = 0;

        foreach (var path in files)
        {
            fileNo++;
            var fileName = Path.GetFileName(path);
            if (alreadyDone.Contains(fileName)) continue;

            try
            {
                var timer = Stopwatch.StartNew();
                var sentences = SentenceSplitter.Split(await File.ReadAllTextAsync(path, ct));
                if (sentences.Count == 0) { Console.WriteLine($"[{fileNo}/{files.Count}] {fileName}: no sentences, skipped"); continue; }

                // The slow part: one embedding per sentence (sent to Ollama in batches).
                var vectors = await embeddings.EmbedDocumentsAsync(sentences, settings.EmbeddingBatchSize, ct);

                for (int i = 0; i < sentences.Count; i++)
                {
                    db.TextEmbeddings.Add(new TextEmbedding
                    {
                        FileName = fileName,
                        SentenceNumber = i,
                        Content = sentences[i],
                        Embedding = new Vector(vectors[i]),
                    });
                }
                await db.SaveChangesAsync(ct);   // one transaction per file
                db.ChangeTracker.Clear();         // keep memory flat across hundreds of files

                sentenceCount += sentences.Count;
                Console.WriteLine($"[{fileNo}/{files.Count}] {fileName}: {sentences.Count} sentences in {timer.Elapsed.TotalSeconds:F1}s");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                Console.WriteLine($"[{fileNo}/{files.Count}] Error processing {fileName}: {ex.Message}");
            }
        }

        Console.WriteLine($"Finished: {sentenceCount:N0} new sentences in {total.Elapsed:hh\\:mm\\:ss}.");
    }
}
