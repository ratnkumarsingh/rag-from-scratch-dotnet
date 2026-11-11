# RAG from Scratch in .NET

A local Retrieval-Augmented Generation (RAG) system in C# / .NET 10, built by following the book
*RAG from Scratch in .NET* (`RAG_from_Scratch_in_DotNET.pdf`). Everything runs on your own machine:

- **Ollama** for the models: `qwen3-embedding:0.6b` (1024-dim embeddings) and `rag-granite`
  (a `granite4.1:3b` model customised by the `Modelfile`)
- **PostgreSQL 16 + pgvector** in Docker as the vector database
- **Microsoft.Extensions.AI** + OllamaSharp, **EF Core** + Pgvector.EntityFrameworkCore

## Projects

| Project | What it is |
|---|---|
| `src/Rag.Core` | The library: corpus download, sentence splitting, ingestion, vector / keyword / hybrid search, window merging, prompt building, LLM re-ranking, evaluation |
| `src/Rag.Cli` | Console app `rag` exposing every step as a command |
| `src/Rag.Web` | Minimal API + single-page chat UI that streams answers with their sources |

## Quick start

```bash
docker compose up -d                       # PostgreSQL + pgvector
ollama pull qwen3-embedding:0.6b
ollama pull granite4.1:3b
ollama create rag-granite -f Modelfile

dotnet build
dotnet run --project src/Rag.Cli -- check      # Ollama + Postgres reachable?
dotnet run --project src/Rag.Cli -- ingest     # embed data/all_articles (~2.5 h on a 4 GB GPU)
dotnet run --project src/Rag.Cli -- index      # HNSW + full-text indexes
dotnet run --project src/Rag.Cli -- ask "Where is the European Court of Human Rights based?" --mode hybrid
dotnet run --project src/Rag.Web               # chat UI on http://localhost:5140
```

Other commands: `similarity`, `corpus --topic "..." --max N`, `extract`, `search`, `eval [--rerank]`, `selftest`.
Run `dotnet run --project src/Rag.Cli` with no arguments for the full list.

**See the [User Guide](docs/USER_GUIDE.md)** for setup details, the web chat, search modes, tips for asking
good questions, adding your own documents, evaluation, configuration and troubleshooting.

## Data

- `data/all_articles/`: 272 Wikipedia articles as plain text (94 on human rights, 178 on international cricket),
  downloaded with `rag corpus`.
- `data/eval/questions.json`: 34 test questions, each paired with the article that should be retrieved.
  `rag eval` reports hit rate and MRR.

Current results (top 5 windows, 34 questions):

| Mode | Hit rate | MRR |
|---|---|---|
| Vector | 65% | 0.41 |
| Hybrid (vector + keyword, RRF) | 82% | 0.71 |

## Deviations from the book

- **Keyword search uses OR instead of AND.** `websearch_to_tsquery` requires every word to appear in one
  sentence, which matched nothing for most natural questions and silently turned hybrid into vector search.
  The terms are now ORed and `ts_rank` puts sentences matching more words first (hybrid went from 71% to 92%
  on the human-rights questions).
- Resumable ingestion that skips files already in the database; `--reset` starts over.
- `hnsw.ef_search` is raised per session so the HNSW index returns the full candidate pool.
- Wikipedia download backs off on HTTP 429 and skips files already on disk.
- `v.` is treated as an abbreviation so case names like "Schalk and Kopf v. Austria" are not split.
- The web API rejects an empty question with HTTP 400.

## Known limitations

- Sentences are embedded on their own, so a sentence like "He made his highest score in 2004..." loses who
  "he" is. Prefixing each sentence with its article title before embedding would fix this (needs a re-ingest).
- Keyword search does not tolerate misspellings ("tendulker" does not match "Tendulkar").
- A 3B model can still invent answers that are not in the sources.

## Attribution

The articles in `data/all_articles` are from [Wikipedia](https://en.wikipedia.org/) and are licensed under
[CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/). Each file is named after its Wikipedia article title.
