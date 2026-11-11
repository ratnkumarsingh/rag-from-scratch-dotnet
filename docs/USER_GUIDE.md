# User Guide

This guide explains how to set up, run and use the RAG (Retrieval-Augmented Generation) system: a local
"ask your documents" assistant. You type a question, the system finds the most relevant passages in your
documents, and a local language model writes a short answer that cites those passages.

Everything runs on your own computer. No question or document is sent to the internet.

---

## 1. How it works (in one minute)

```
 your question
      │
      ▼
 ① embed the question ──► ② search the vector DB (+ keyword search in hybrid mode)
                                  │
                                  ▼
                       ③ take the best sentences + 3 sentences either side ("windows")
                                  │
                                  ▼
                       ④ send up to 5 windows + the question to the chat model
                                  │
                                  ▼
                       ⑤ answer with [Source n] citations
```

- **Ingestion** (done once): every `.txt` file in `data/all_articles` is split into sentences, each sentence is
  turned into a vector of 1,024 numbers (an *embedding*) that captures its meaning, and stored in PostgreSQL.
- **Answering**: your question is embedded the same way, and the closest sentences are retrieved.
  The chat model is instructed to use **only** those passages. If they don't contain the answer, it replies:
  *"Given the information provided, I'm unable to answer your question."*

---

## 2. Requirements

| What | Version / notes |
|---|---|
| .NET SDK | 10 |
| Docker Desktop | for PostgreSQL 16 + pgvector |
| Ollama | runs the models locally ([ollama.com](https://ollama.com)) |
| GPU | optional but strongly recommended; developed on a 4 GB GTX 960M |
| Disk | ~3 GB for the models, ~300 MB for the database |

---

## 3. First-time setup

Run these from the repository root.

```bash
# 1. Start the database (PostgreSQL + pgvector). Data survives restarts in a Docker volume.
docker compose up -d

# 2. Download the models
ollama pull qwen3-embedding:0.6b        # embeddings (1024 dimensions)
ollama pull granite4.1:3b               # chat model base

# 3. Build the RAG chat model (adds the rules and refusal sentence from the Modelfile)
ollama create rag-granite -f Modelfile

# 4. Build the solution and check everything is reachable
dotnet build
dotnet run --project src/Rag.Cli -- check
```

`check` should print something like:

```
Ollama:     http://localhost:11434
  chat model  'rag-granite' replied 'OK' in 3.2s
  embed model 'qwen3-embedding:0.6b' -> 1024 dimensions
PostgreSQL: connected
```

### Load the documents into the vector database

The repository already contains 272 Wikipedia articles (human rights and international cricket) in
`data/all_articles`.

```bash
dotnet run --project src/Rag.Cli -- ingest     # ~1.5–2.5 hours on a 4 GB GPU for ~30,000 sentences
dotnet run --project src/Rag.Cli -- index      # adds the search indexes (under a minute)
```

Ingestion is **resumable**: if you stop it (Ctrl+C) or it crashes, run `ingest` again and it continues with
the files not yet stored. Use `ingest --reset` only when you want to empty the database and start over.

> **Tip:** close other GPU users while ingesting. Chatting with another model in the Ollama desktop app
> loads it onto the GPU and can halve the ingest speed. Check with `ollama ps`, unload with `ollama stop <model>`.

---

## 4. Using the web chat

```bash
dotnet run --project src/Rag.Web
```

Open **http://localhost:5140**.

1. Type a question.
2. Pick a search **mode** (see below). **Hybrid** is the best choice for most questions.
3. Press **Ask**. The sources appear first, then the answer streams in word by word.

The grey line under each answer lists the sources, e.g.
`[Source 1] European Court of Human Rights (sentences 0-5)`: the article name and the sentence range that
was given to the model. The answer's `[Source n]` citations refer to this list.

### Search modes

| Mode | What it does | When to use |
|---|---|---|
| `vector` | Finds sentences with a similar *meaning* | Paraphrased or conceptual questions |
| `hybrid` | Vector search **plus** keyword search, merged with Reciprocal Rank Fusion | **Default choice**: much better with names, dates and rare terms |
| `hybrid + rerank` | Hybrid, then the LLM scores 10 windows and keeps the best 5 | Hard questions; adds ~10 model calls, so it is slow |

On the 34 test questions: vector finds the right article 65% of the time, hybrid 82%.

---

## 5. Using the command line

All commands: `dotnet run --project src/Rag.Cli -- <command> [options]`
(add `--no-build` to start faster after the first build).

| Command | What it does |
|---|---|
| `check` | Tests the Ollama and PostgreSQL connections |
| `similarity "a" "b" ["c" ...]` | Shows how similar sentences are in meaning (0 to 1) |
| `corpus [--topic "..."] [--max N]` | Downloads Wikipedia articles on a topic into `data/all_articles` |
| `extract [file\|folder]` | Converts PDF / DOCX / PPTX to `.txt` (default folder `data/my_docs`) |
| `ingest [--reset] [--max-files N]` | Splits, embeds and stores every new `.txt` file |
| `search "query" [--k 5]` | Shows the raw nearest sentences and their distance (lower = closer) |
| `ask "question" [--mode vector\|hybrid\|rerank] [--show-prompt]` | Answers a question |
| `index` | Creates the HNSW (vector) and GIN (full-text) indexes |
| `eval [--file path] [--rerank]` | Measures retrieval quality on test questions |
| `selftest` | Runs offline logic checks (no Ollama or database needed) |

Examples:

```bash
dotnet run --project src/Rag.Cli -- ask "Where is the European Court of Human Rights based?" --mode hybrid
dotnet run --project src/Rag.Cli -- ask "Who won the 1983 Cricket World Cup?" --mode hybrid --show-prompt
dotnet run --project src/Rag.Cli -- search "Imperial Cricket Conference" --k 10
```

`--show-prompt` prints the exact text sent to the model, which is the best way to see *why* an answer is
right or wrong.

---

## 6. Asking good questions

- **The answer must be in the documents.** Questions outside the corpus (e.g. football) get the refusal
  sentence; that is the system working correctly.
- **You don't need the exact wording of the articles.** Retrieval is by meaning, so
  "Where is the ECHR located?" and "In which city does the Strasbourg Court sit?" find the same passage.
- **Be specific.** "Tendulkar's highest Test score" works better than "Tendulkar top score".
  Only 5 windows reach the model, so a vague question spreads them across many articles.
- **Spell names correctly.** Keyword search matches exact words: "tendulker" won't match "Tendulkar".
- **Use hybrid mode** for names, dates, organisations and other specific terms.
- **Check the sources.** If the sources look unrelated, the answer is probably a refusal or a guess. Rephrase.

### How do I know the answer came from my documents?

- Every answer cites `[Source n]` labels that match the sources line.
- The chat model has no internet access, and its instructions forbid outside knowledge.
- Run `ask` with `--show-prompt` to see exactly what the model was given.

---

## 7. Adding your own documents or topics

**More Wikipedia articles:**

```bash
dotnet run --project src/Rag.Cli -- corpus --topic "renewable energy" --max 100
dotnet run --project src/Rag.Cli -- ingest      # only the new files are embedded
```

Afterwards, look through the new files and delete anything off-topic (e.g. video games, TV channels)
*before* ingesting. Downloads that hit Wikipedia's rate limit can simply be re-run; existing files are skipped.

**Your own PDF / Word / PowerPoint files:**

```bash
# put the files in data/my_docs, then:
dotnet run --project src/Rag.Cli -- extract
dotnet run --project src/Rag.Cli -- ingest
```

All topics share one database; retrieval picks the relevant articles automatically.

**Removing a document:** delete the `.txt` file and run `ingest --reset` (re-embeds everything), or delete its
rows directly: `DELETE FROM text_embeddings WHERE file_name = 'Some_Article.txt';`

---

## 8. Measuring quality (evaluation)

`data/eval/questions.json` holds test questions, each paired with the article that should be retrieved:

```json
{ "question": "In which city is the Gaddafi Stadium?", "expectedFile": "Gaddafi_Stadium.txt" }
```

```bash
dotnet run --project src/Rag.Cli -- eval            # vector vs hybrid
dotnet run --project src/Rag.Cli -- eval --rerank   # adds rerank (slow: ~10 model calls per question)
```

- **Hit rate:** how often the expected article is among the 5 sources.
- **MRR (mean reciprocal rank):** rewards finding it near the top (1.0 = always first).

The metric is strict: a "miss" can still produce a correct answer when another article contains the same fact.
Add questions for your own documents to keep an eye on quality after changes.

---

## 9. Configuration

Every setting can be overridden with an environment variable (defaults in `src/Rag.Core/RagSettings.cs`):

| Variable | Default | Meaning |
|---|---|---|
| `RAG_OLLAMA_URL` | `http://localhost:11434` | Ollama server |
| `RAG_CHAT_MODEL` | `rag-granite` | Model that writes the answers |
| `RAG_BASE_MODEL` | `granite4.1:3b` | Model used for re-ranking |
| `RAG_EMBED_MODEL` | `qwen3-embedding:0.6b` | Embedding model |
| `RAG_EMBED_DIMS` | `1024` | Must match the embedding model |
| `RAG_PG` | `Host=localhost;Port=5432;Database=text_embeddings;Username=postgres;Password=postgres` | Database connection |
| `RAG_CORPUS` | `data/all_articles` | Folder of `.txt` files to ingest |

> Changing the **embedding model** means every stored vector is incompatible: run `ingest --reset`.
> Changing the **chat model** needs no re-ingest. To keep the citation and refusal rules, build your model
> from a copy of the `Modelfile` with a different `FROM` line.

Example (PowerShell):

```powershell
$env:RAG_CHAT_MODEL = "my-rag-model"
dotnet run --project src/Rag.Web
```

Retrieval settings (window size 3, 5 windows, 50 candidates) are in `RagSettings.cs`.

---

## 10. Troubleshooting

| Problem | Cause / fix |
|---|---|
| `Could not reach Ollama` | Start Ollama (tray icon on Windows, or `ollama serve`) |
| `PostgreSQL: NOT reachable` | `docker compose up -d`; check `docker ps` shows `rag-postgres` |
| `model 'rag-granite' not found` | `ollama create rag-granite -f Modelfile` |
| Answers or ingest very slow | Another model is on the GPU: `ollama ps`, then `ollama stop <name>` |
| Sources unrelated to the question | Normal for questions not covered by the documents: the 5 closest passages are always shown, and the model should refuse. Otherwise try hybrid mode, fix spelling, be more specific |
| Answer contains a fact not in the sources | Small (3B) models occasionally invent details. Check the sources; rephrase; try `hybrid + rerank` |
| Web page says `HTTP 400` | The question was empty |
| Port 5140 already in use | Another copy is running; stop it, or set `ASPNETCORE_URLS=http://localhost:5150` |
| Want to start completely fresh | `docker compose down -v` (deletes the database), `docker compose up -d`, then `ingest` and `index` |

---

## 11. Known limitations

- Each sentence is embedded on its own, so a sentence like *"He made his highest score in 2004…"* doesn't
  know who "he" is and can be missed.
- Keyword search does not tolerate spelling mistakes.
- Facts mentioned in many articles (e.g. the founding of the ICC) compete for the 5 windows, so the most
  relevant article can be crowded out.
- Leftover table headings from Wikipedia (e.g. "Most Test wickets for India") can match questions about
  records without containing the facts.
