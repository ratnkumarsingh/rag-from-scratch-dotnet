using System.Text.Json;
using Rag.Core;
using Rag.Core.Ai;
using Rag.Core.Data;
using Rag.Core.Generation;
using Rag.Core.Retrieval;

// Phase 5: a tiny web chat UI on top of the same Rag.Core library the console app uses.
// Run:  dotnet run --project src/Rag.Web    then open the URL it prints (http://localhost:5140)

var builder = WebApplication.CreateBuilder(args);

var settings = new RagSettings();
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(_ => new EmbeddingService(AiClients.CreateEmbeddingGenerator(settings)));
builder.Services.AddScoped<RagDbContext>();
builder.Services.AddScoped<Retriever>();
builder.Services.AddScoped(sp => new RagAnswerer(
    sp.GetRequiredService<Retriever>(),
    AiClients.CreateChatClient(settings),
    settings,
    new LlmReranker(AiClients.CreateChatClient(settings, settings.BaseChatModel))));

var app = builder.Build();
app.UseDefaultFiles();   // serves wwwroot/index.html at "/"
app.UseStaticFiles();

// POST /api/ask  { "question": "...", "mode": "vector" | "hybrid" | "rerank" }
// Response (streamed text): first line = JSON array of sources, then the answer tokens as they arrive.
app.MapPost("/api/ask", async (AskRequest request, RagAnswerer rag, HttpResponse response, CancellationToken ct) =>
{
    // Deviation from the book: reject an empty question instead of sending it to the models.
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        response.StatusCode = StatusCodes.Status400BadRequest;
        await response.WriteAsync("Question is required.", ct);
        return;
    }

    var mode = request.Mode?.ToLowerInvariant() switch
    {
        "hybrid" => RetrievalMode.Hybrid,
        "rerank" => RetrievalMode.HybridRerank,
        _ => RetrievalMode.Vector,
    };

    var context = await rag.RetrieveAsync(request.Question, mode, ct);

    response.ContentType = "text/plain; charset=utf-8";
    await response.WriteAsync(JsonSerializer.Serialize(context.Sources) + "\n", ct);
    await response.Body.FlushAsync(ct);

    await foreach (var token in rag.AnswerAsync(context, ct))
    {
        await response.WriteAsync(token, ct);
        await response.Body.FlushAsync(ct);   // push each token to the browser immediately
    }
});

app.Run();

public sealed record AskRequest(string Question, string? Mode);
