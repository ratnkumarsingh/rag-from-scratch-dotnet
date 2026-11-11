# Installation Guide

A complete, step-by-step guide to install everything this project needs and get from a fresh machine to a
working "ask your documents" chat. Windows is the primary platform (the project was built and tested there);
macOS and Linux commands are given where they differ.

**Time needed:** about 30 minutes of installing + 1.5–2.5 hours of unattended ingestion (GPU-dependent).

---

## 1. Hardware requirements

| | Minimum | Recommended | Tested on |
|---|---|---|---|
| CPU | 64-bit, 4 cores | 8 cores | Intel laptop CPU |
| RAM | 8 GB | 16 GB+ | 32 GB |
| GPU | none (CPU works, but ingestion takes many hours) | NVIDIA with 4 GB+ VRAM, or Apple Silicon | NVIDIA GeForce GTX 960M, 4 GB, driver 566.03 |
| Disk space | 6 GB free | 10 GB free | |

Disk space breakdown: models ~2.8 GB, PostgreSQL image ~0.6 GB, database ~0.3 GB, .NET SDK + Docker the rest.

---

## 2. Software needed

### Required

| Software | Tested version | What it's for | Download |
|---|---|---|---|
| **Git** | 2.45 | Cloning the repository | [git-scm.com](https://git-scm.com/downloads) |
| **.NET SDK** | 10.0.403 (any 10.0.x) | Building and running the C# projects | [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **Docker Desktop** (Windows/macOS) or **Docker Engine + Compose** (Linux) | Docker 29.9, Compose v5.7 | Runs PostgreSQL in a container | [docker.com](https://www.docker.com/products/docker-desktop/) |
| **Ollama** | 0.40.15 | Runs the AI models locally | [ollama.com](https://ollama.com/download) |
| **WSL 2** (Windows only) | | Required by Docker Desktop on Windows | built into Windows 10/11 |

### Pulled automatically by the steps below

| Component | Version | Size | Role |
|---|---|---|---|
| `pgvector/pgvector:pg16` Docker image | PostgreSQL 16.15 + pgvector 0.8.7 | ~620 MB | Vector database |
| `qwen3-embedding:0.6b` Ollama model | | ~640 MB | Turns text into 1,024-number vectors |
| `granite4.1:3b` Ollama model | | ~2.1 GB | Base chat model, also used for re-ranking |
| `rag-granite` Ollama model | built locally from `Modelfile` | (shares granite's files) | The answering model with citation/refusal rules |

### NuGet packages (restored automatically by `dotnet build`)

| Package | Version | Used in |
|---|---|---|
| OllamaSharp | 5.5.2 | Talking to Ollama via Microsoft.Extensions.AI (`IChatClient`, `IEmbeddingGenerator`) |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.0 | EF Core database access |
| Pgvector.EntityFrameworkCore | 0.3.0 | `vector` column type and cosine distance |
| PdfPig | 0.1.16 | Extracting text from PDFs |
| DocumentFormat.OpenXml | 3.3.0 | Extracting text from Word and PowerPoint files |

### Optional

| Software | Why you might want it |
|---|---|
| **NVIDIA GPU driver** (latest) | Lets Ollama use an NVIDIA GPU. Ollama detects it automatically. |
| **Visual Studio 2026** (".NET desktop / ASP.NET" workloads) or **VS Code + C# Dev Kit** | Editing and debugging. Open `RagFromScratch.slnx`. |
| **GitHub CLI (`gh`)** | Cloning / forking from the command line |
| **DBeaver** or **pgAdmin** | Browsing the `text_embeddings` table (host `localhost`, port `5432`, user/password `postgres`) |

---

## 3. Install the software

### Windows 10 / 11

Open **PowerShell as Administrator**.

**Step 1: WSL 2** (needed by Docker Desktop; skip if `wsl --status` already shows version 2)

```powershell
wsl --install
```

Restart Windows when asked.

**Step 2: Git, .NET 10 SDK, Docker Desktop and Ollama** using `winget` (built into Windows):

```powershell
winget install --id Git.Git -e
winget install --id Microsoft.DotNet.SDK.10 -e
winget install --id Docker.DockerDesktop -e
winget install --id Ollama.Ollama -e
```

Prefer installers? Download each from the links in section 2 and accept the defaults.

**Step 3: Start Docker Desktop** from the Start menu once, accept the terms, and wait until it shows
"Engine running". In *Settings → General*, tick "Use the WSL 2 based engine" (default).

**Step 4: Ollama** starts automatically after install (llama icon in the system tray). If not, start "Ollama"
from the Start menu.

**Step 5: Open a new terminal** (so the new programs are on your PATH) and verify:

```powershell
git --version          # git version 2.x
dotnet --version       # 10.0.x
docker --version       # Docker version 2x.x
docker compose version # Docker Compose version v2.x or later
ollama --version       # ollama version is 0.x
```

### macOS (Apple Silicon recommended)

Using [Homebrew](https://brew.sh):

```bash
brew install git
brew install --cask dotnet-sdk
brew install --cask docker        # Docker Desktop; open it once from Applications
brew install --cask ollama        # or download from ollama.com; open it once
```

Apple Silicon GPUs are used by Ollama automatically. Verify with the same commands as Windows step 5.

### Linux (Ubuntu / Debian)

```bash
# Git
sudo apt update && sudo apt install -y git

# .NET 10 SDK (see https://learn.microsoft.com/dotnet/core/install/linux for other distros)
sudo apt install -y dotnet-sdk-10.0

# Docker Engine + Compose plugin (see https://docs.docker.com/engine/install/ for other distros)
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER     # log out and back in afterwards

# Ollama (installs a systemd service)
curl -fsSL https://ollama.com/install.sh | sh
```

For an NVIDIA GPU, install the proprietary driver (`sudo ubuntu-drivers install`) before Ollama.

---

## 4. Get the project

```bash
git clone https://github.com/ratnkumarsingh/rag-from-scratch-dotnet.git
cd rag-from-scratch-dotnet
```

All the following commands are run from this folder.

---

## 5. Start the database

```bash
docker compose up -d
```

This downloads `pgvector/pgvector:pg16` (first time only), starts a container named `rag-postgres` on port
**5432**, creates the database `text_embeddings` (user `postgres`, password `postgres`) and enables the `vector`
extension via `db/init.sql`. Data is kept in the Docker volume `pgdata`, so it survives restarts.

Verify:

```bash
docker ps        # rag-postgres should be "Up"
```

> **Port 5432 already used?** You probably have PostgreSQL installed locally. Either stop it, or change
> `"5432:5432"` to `"5433:5432"` in `compose.yaml` and set
> `RAG_PG=Host=localhost;Port=5433;Database=text_embeddings;Username=postgres;Password=postgres`.

---

## 6. Download and create the models

```bash
ollama pull qwen3-embedding:0.6b
ollama pull granite4.1:3b
ollama create rag-granite -f Modelfile
```

Verify:

```bash
ollama list
```

You should see `qwen3-embedding:0.6b`, `granite4.1:3b` and `rag-granite:latest`.

The `Modelfile` makes answers deterministic (temperature 0, seed 42), sets an 8,192-token context window, and
adds the rules: answer only from the provided context, cite `[Source n]`, and otherwise reply exactly
*"Given the information provided, I'm unable to answer your question."*

---

## 7. Build and test

```bash
dotnet build
dotnet run --project src/Rag.Cli -- selftest
dotnet run --project src/Rag.Cli -- check
```

- `build` restores the NuGet packages and should end with **Build succeeded**.
- `selftest` runs 24 offline checks (sentence splitting, cosine maths, window merging, rank fusion) and should
  report all passed.
- `check` should print:

```
Ollama:     http://localhost:11434
  chat model  'rag-granite' replied 'OK' in 3.2s
  embed model 'qwen3-embedding:0.6b' -> 1024 dimensions
PostgreSQL: connected
```

---

## 8. Load the documents (ingestion)

The repository includes 272 Wikipedia articles in `data/all_articles` (94 human rights, 178 international
cricket; about 30,000 sentences).

```bash
dotnet run --project src/Rag.Cli -- ingest
```

| Hardware | Approximate time |
|---|---|
| NVIDIA GTX 960M, 4 GB | ~2.5 hours (2.5–3.5 sentences/s) |
| Modern GPU (RTX 30/40 series) or Apple M-series | 15–45 minutes |
| CPU only | many hours |

- Progress is printed per file. You can stop with **Ctrl+C** and run `ingest` again later: files already stored
  are skipped.
- Keep the GPU free: don't chat with other models in the Ollama app meanwhile (`ollama ps` shows loaded
  models; `ollama stop <name>` unloads one).
- Want a quick trial first? `ingest --max-files 5` loads only the first 5 files.

Then create the search indexes (under a minute):

```bash
dotnet run --project src/Rag.Cli -- index
```

---

## 9. Run it

**Command line:**

```bash
dotnet run --project src/Rag.Cli -- ask "Where is the European Court of Human Rights based?" --mode hybrid
```

Expected: an answer like *"…based in Strasbourg, France [Source 1]."* after the list of sources.

**Web chat:**

```bash
dotnet run --project src/Rag.Web
```

Open **http://localhost:5140**, choose **hybrid** and ask a question.

**Measure quality (optional):**

```bash
dotnet run --project src/Rag.Cli -- eval
```

Expected roughly: Vector 65% hit rate / 0.41 MRR, Hybrid 82% / 0.71.

Installation is complete. See the [User Guide](USER_GUIDE.md) for everyday use.

---

## 10. Daily start / stop

| To... | Run |
|---|---|
| Start after a reboot | Start Docker Desktop and Ollama (both can auto-start), then `docker compose up -d` |
| Run the web chat | `dotnet run --project src/Rag.Web` |
| Stop the web chat | Ctrl+C in its terminal |
| Stop the database | `docker compose down` (data is kept) |
| Free GPU memory | `ollama stop rag-granite` (models also unload by themselves after 5 idle minutes) |

---

## 11. Installation troubleshooting

| Symptom | Fix |
|---|---|
| `dotnet: command not found` / wrong version | Open a new terminal after installing; `dotnet --list-sdks` must show a 10.0.x SDK |
| `error NETSDK1045: The current .NET SDK does not support targeting .NET 10.0` | Install the .NET 10 SDK (not just the runtime) |
| Docker: "WSL 2 installation is incomplete" | Run `wsl --install` (or `wsl --update`) as Administrator and restart |
| `docker compose up` fails: "Cannot connect to the Docker daemon" | Docker Desktop isn't running; start it and wait for "Engine running" |
| `check`: "Could not reach Ollama" | Start Ollama (tray icon / `ollama serve`); test with `curl http://localhost:11434` (should say "Ollama is running") |
| `check`: model not found | Re-run the three commands in section 6 |
| `check`: "PostgreSQL: NOT reachable" | `docker ps` should show `rag-postgres` Up; check port 5432 isn't used by another PostgreSQL |
| Ingestion is extremely slow | Ollama is on the CPU or sharing the GPU. `ollama ps` shows `100% GPU` when loaded correctly; update the GPU driver; close other model chats |
| Ollama out of memory during ingest | Lower `EmbeddingBatchSize` (default 32) in `src/Rag.Core/RagSettings.cs` |
| `ingest` error: `extension "vector" is not available` | You're connected to a PostgreSQL without pgvector (e.g. a local install on port 5432). Stop it, or move the container to another port (see section 5) |
| Antivirus slows `dotnet build` | Exclude the project folder from real-time scanning |

---

## 12. Uninstall / clean up

```bash
docker compose down -v                 # removes the container AND the database volume
docker image rm pgvector/pgvector:pg16
ollama rm rag-granite granite4.1:3b qwen3-embedding:0.6b
```

Then uninstall Ollama, Docker Desktop and the .NET SDK from *Settings → Apps* (Windows) if you no longer need
them, and delete the project folder.
