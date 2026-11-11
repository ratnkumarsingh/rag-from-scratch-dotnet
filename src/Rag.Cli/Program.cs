using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Rag.Core;
using Rag.Core.Ai;
using Rag.Core.Corpus;
using Rag.Core.Data;
using Rag.Core.Evaluation;
using Rag.Core.Generation;
using Rag.Core.Ingestion;
using Rag.Core.Retrieval;
using Rag.Core.Text;

// --------------------------------------------------------------------------------------------
//  rag <command> [arguments]          (run from the repository root: dotnet run --project src/Rag.Cli -- <command>)
//
//  Phase 2  check                       Is Ollama + Postgres reachable? Are the models there?
//           similarity "a" "b" ["c"..]  Embed sentences and print how similar they are
//           corpus [--topic t] [--max n] Download Wikipedia articles into data/all_articles
//           extract <file|folder>       Turn PDF / DOCX / PPTX into .txt in data/all_articles
//  Phase 3  ingest [--reset] [--max-files n]   Split, embed and store every .txt file
//           search "query" [--k n]      Show the raw nearest sentences
//  Phase 4  ask "question" [--show-prompt] [--mode vector|hybrid|rerank]
//  Phase 5  index                       Create HNSW (vector) + GIN (full-text) indexes
//           eval [--file path] [--rerank]  Hit rate / MRR for vector vs hybrid (vs rerank)
//  Any     selftest                    Run the offline unit checks (no Ollama/DB needed)
// --------------------------------------------------------------------------------------------

var settings = new RagSettings();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var ct = cts.Token;

if (args.Length == 0) { PrintHelp(); return 0; }

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "check": await CheckAsync(); break;
        case "similarity": await SimilarityAsync(args[1..]); break;
        case "corpus": await CorpusAsync(); break;
        case "extract": Extract(); break;
        case "ingest": await IngestAsync(); break;
        case "search": await SearchAsync(); break;
        case "ask": await AskAsync(); break;
        case "index": await IndexAsync(); break;
        case "eval": await EvalAsync(); break;
        case "selftest": return SelfTest.Run();
        default: PrintHelp(); return 1;
    }
    return 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nCancelled.");
    return 130;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Could not reach Ollama at {settings.OllamaUrl}: {ex.Message}");
    Console.Error.WriteLine("Is Ollama running? (Windows: check the tray icon, or run 'ollama serve')");
    return 1;
}

// ============================================================================ Phase 2

async Task CheckAsync()
{
    Console.WriteLine($"Ollama:     {settings.OllamaUrl}");
    var chat = AiClients.CreateChatClient(settings);
    var sw = Stopwatch.StartNew();
    var reply = await chat.GetResponseAsync("Reply with exactly: OK", cancellationToken: ct);
    Console.WriteLine($"  chat model  '{settings.ChatModel}' replied '{reply.Text.Trim()}' in {sw.Elapsed.TotalSeconds:F1}s");

    var embeddings = new EmbeddingService(AiClients.CreateEmbeddingGenerator(settings));
    var vector = await embeddings.EmbedRawAsync("hello world", ct);
    Console.WriteLine($"  embed model '{settings.EmbeddingModel}' -> {vector.Length} dimensions" +
                      (vector.Length == settings.EmbeddingDimensions ? "" : $"  (!) settings expect {settings.EmbeddingDimensions}"));

    await using var db = new RagDbContext(settings);
    Console.WriteLine($"PostgreSQL: {(await db.Database.CanConnectAsync(ct) ? "connected" : "NOT reachable")}");
}

async Task SimilarityAsync(string[] sentences)
{
    if (sentences.Length < 2) { Console.WriteLine("Give at least two sentences in quotes."); return; }
    var embeddings = new EmbeddingService(AiClients.CreateEmbeddingGenerator(settings));
    var vectors = new List<float[]>();
    foreach (var s in sentences) vectors.Add(await embeddings.EmbedRawAsync(s, ct));

    Console.WriteLine($"Each sentence became a vector of {vectors[0].Length} numbers. First 5 of #1: " +
                      string.Join(", ", vectors[0].Take(5).Select(v => v.ToString("F3"))) + ", ...\n");
    Console.WriteLine("Cosine similarity (1.00 = same meaning, ~0 = unrelated):");
    for (int i = 0; i < sentences.Length; i++)
        for (int j = i + 1; j < sentences.Length; j++)
            Console.WriteLine($"  {VectorMath.CosineSimilarity(vectors[i], vectors[j]):F2}  \"{sentences[i]}\"  vs  \"{sentences[j]}\"");
}

async Task CorpusAsync()
{
    var topic = Option("--topic") ?? "human rights";
    var max = int.Parse(Option("--max") ?? "100");
    using var generator = new WikipediaCorpusGenerator();
    await generator.GenerateAsync(topic, max, settings.CorpusFolder, ct);
}

void Extract()
{
    var input = args.Length > 1 ? args[1] : Path.Combine("data", "my_docs");
    Console.WriteLine($"Extracting '{input}' -> '{settings.CorpusFolder}'");
    var n = DocumentTextExtractor.ExtractToFolder(input, settings.CorpusFolder);
    Console.WriteLine($"{n} file(s) extracted.");
}

// ============================================================================ Phase 3

async Task IngestAsync()
{
    await using var db = new RagDbContext(settings);
    var embeddings = new EmbeddingService(AiClients.CreateEmbeddingGenerator(settings));
    var maxFiles = Option("--max-files") is { } m ? int.Parse(m) : (int?)null;
    await new Ingestor(db, embeddings, settings).IngestFolderAsync(settings.CorpusFolder, Flag("--reset"), maxFiles, ct);
}

async Task SearchAsync()
{
    var query = RequireText("search");
    var k = int.Parse(Option("--k") ?? "5");
    await using var db = new RagDbContext(settings);
    var embeddings = new EmbeddingService(AiClients.CreateEmbeddingGenerator(settings));
    var retriever = new Retriever(db, embeddings, settings);

    var hits = await retriever.VectorSearchAsync(await embeddings.EmbedQueryAsync(query, ct), k, ct);
    Console.WriteLine($"Top {hits.Count} sentences for: {query}\n");
    foreach (var h in hits)
        Console.WriteLine($"  {h.Score:F3}  {h.FileName} #{h.SentenceNumber}\n         {h.Content}\n");
}

// ============================================================================ Phase 4

async Task AskAsync()
{
    var question = RequireText("ask");
    await using var db = new RagDbContext(settings);
    var rag = CreateAnswerer(db);

    var sw = Stopwatch.StartNew();
    var context = await rag.RetrieveAsync(question, ParseMode(), ct);
    Console.WriteLine($"Retrieved {context.Windows.Count} context window(s) in {sw.Elapsed.TotalSeconds:F1}s:");
    foreach (var s in context.Sources) Console.WriteLine("   " + s);

    if (Flag("--show-prompt"))
        Console.WriteLine($"\n----- PROMPT SENT TO THE LLM -----\n{context.Prompt}\n----------------------------------");

    Console.WriteLine("\nAnswer:");
    sw.Restart();
    await foreach (var token in rag.AnswerAsync(context, ct)) Console.Write(token);
    Console.WriteLine($"\n\n({sw.Elapsed.TotalSeconds:F1}s)");
}

// ============================================================================ Phase 5

async Task IndexAsync()
{
    await using var db = new RagDbContext(settings);
    Console.WriteLine("Creating HNSW index on embedding (approximate nearest neighbour search)...");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS ix_text_embeddings_hnsw ON text_embeddings USING hnsw (embedding vector_cosine_ops);", ct);
    Console.WriteLine("Creating GIN index for full-text (keyword) search...");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS ix_text_embeddings_fts ON text_embeddings USING gin (to_tsvector('english', content));", ct);
    Console.WriteLine("Done.");
}

async Task EvalAsync()
{
    var file = Option("--file") ?? Path.Combine("data", "eval", "questions.json");
    var questions = Evaluator.Load(file);

    // A question whose expected file is not in the corpus can never be a hit: warn and skip it.
    var missing = questions.Where(q => !File.Exists(Path.Combine(settings.CorpusFolder, q.ExpectedFile))).ToList();
    foreach (var q in missing) Console.WriteLine($"  (skipped: '{q.ExpectedFile}' is not in {settings.CorpusFolder})");
    questions = questions.Except(missing).ToList();
    if (questions.Count == 0) { Console.WriteLine("No usable questions. Edit the eval file to match your corpus."); return; }

    await using var db = new RagDbContext(settings);
    var evaluator = new Evaluator(CreateAnswerer(db));

    var results = new List<(RetrievalMode Mode, double HitRate, double Mrr)>();
    var modes = Flag("--rerank")
        ? new[] { RetrievalMode.Vector, RetrievalMode.Hybrid, RetrievalMode.HybridRerank }
        : new[] { RetrievalMode.Vector, RetrievalMode.Hybrid };

    foreach (var mode in modes)
    {
        var (hitRate, mrr) = await evaluator.RunAsync(questions, mode, ct);
        results.Add((mode, hitRate, mrr));
        Console.WriteLine();
    }

    Console.WriteLine("Summary");
    Console.WriteLine("  Mode           Hit rate   MRR");
    foreach (var r in results) Console.WriteLine($"  {r.Mode,-14} {r.HitRate,8:P0}   {r.Mrr:F2}");
}

// ============================================================================ helpers

RetrievalMode ParseMode() => (Option("--mode") ?? "vector").ToLowerInvariant() switch
{
    "hybrid" => RetrievalMode.Hybrid,
    "rerank" => RetrievalMode.HybridRerank,
    _ => RetrievalMode.Vector,
};


RagAnswerer CreateAnswerer(RagDbContext db)
{
    var embeddings = new EmbeddingService(AiClients.CreateEmbeddingGenerator(settings));
    var retriever = new Retriever(db, embeddings, settings);
    var reranker = new LlmReranker(AiClients.CreateChatClient(settings, settings.BaseChatModel));
    return new RagAnswerer(retriever, AiClients.CreateChatClient(settings), settings, reranker);
}


string RequireText(string command)
{
    var text = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
    if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException($"Usage: rag {command} \"your text\"");
    return text;
}

string? Option(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

bool Flag(string name) => args.Contains(name);

void PrintHelp() => Console.WriteLine("""
    RAG from scratch: commands
      check                              test Ollama + PostgreSQL connections
      similarity "s1" "s2" ["s3" ...]    compare sentence meanings
      corpus [--topic "human rights"] [--max 100]
      extract [file|folder]              PDF/DOCX/PPTX -> .txt (default folder: data/my_docs)
      ingest [--reset] [--max-files N]
      search "query" [--k 5]
      ask "question" [--show-prompt] [--mode vector|hybrid|rerank]
      index                              create HNSW + full-text indexes
      eval [--file data/eval/questions.json] [--rerank]
      selftest                           offline logic checks
    """);
