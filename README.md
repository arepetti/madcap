# Multi-Agent Debate

**MADCAP** (Multi-Agent Debate with a Critic-And-Profile loop) is an interactive CLI that answers your questions by running a small structured debate between three LLMs instead of asking one model and trusting the reply. A confident **Answerer** drafts a response, a sceptical **Critic** stress-tests it, and an impartial **Judge** mediates the exchange and writes the final verdict. The user sees the rephrased question, every round of debate, and the verdict with a confidence label. For benchmark prompts and how to test, evaluate, and compare configurations via `!stats`, see [testing.md](testing.md).

The Judge does extra work that is the point of the system: it rewrites the user's question into a neutral form before anyone sees it (to dampen prompt-side bias), restates every Answerer turn as plain facts before the Critic ever sees them (so the Critic argues with the *claim*, not the prose), and quietly builds a profile of the Answerer's recurring weaknesses across rounds so future Critics know where to probe harder. The full design rationale, references and known issues live in [design.md](design.md); the code map and extension points are in [architecture.md](architecture.md).

This is a toy project for experimenting with multi-agent debate protocols, not a production system.

Fair warning: this is a work-in-progress. Changes get pushed to the repo whenever something interesting occurs to me, and they might break existing functionality without notice. The latest version might not even work at all. This is a playground for experimentation, not a stable library. Treat it accordingly.

## Prerequisites

- Windows 10/11 with [winget](https://learn.microsoft.com/en-us/windows/package-manager/winget/) (the bundled installer is PowerShell-only; on macOS / Linux follow the *Manual install* section below).
- Python 3.10 or newer on `PATH`.
- About 9 GB of free disk for the default Ollama models (`llama3.1:8b` + `qwen2.5:7b`).
- An Ollama-friendly machine: 16 GB RAM is comfortable for 7–8B models; less will work but slowly.

## Quick start (Windows)

From the repo root:

```powershell
cd src
.\install.ps1
.\.venv\Scripts\Activate.ps1
python debate.py
```

The installer pulls Ollama, sets `OLLAMA_NUM_CTX=8192`, downloads the default models, creates `.venv` and installs the Python requirements.

## Manual install (macOS / Linux / no winget)

1. Install [Ollama](https://ollama.com/download) for your platform and start it (`ollama serve`).
2. Pull the default models:
  ```bash
   ollama pull llama3.1:8b
   ollama pull qwen2.5:7b
  ```
3. Set the context window once, in your shell profile, so `!stats` reports correct numbers:
  ```bash
   export OLLAMA_NUM_CTX=8192
  ```
4. Create a venv and install requirements:
  ```bash
   cd src
   python -m venv .venv
   source .venv/bin/activate
   pip install -r requirements.txt
  ```
5. Run:
  ```bash
   python debate.py
  ```

## Using it

Once started, type a question and watch the round play out. A few built-in commands are available at the prompt:


| Command     | What it does                                                    |
| ----------- | --------------------------------------------------------------- |
| `!help`     | List commands.                                                  |
| `!new`      | Start a fresh session (wipe Answerer memory, history, profile). |
| `!personas` | Show role -> model -> temperature -> persona file.              |
| `!stats`    | Per-actor context-window usage, profile state, history.         |
| `<empty>`   | Exit.                                                           |


Anything that does not start with `!` is treated as a question for the debate.

## Extending and configuring

Adding commands, swapping models, switching from local Ollama to a hosted API (OpenAI, Anthropic, Azure, ...), adding personas and tweaking the debate pipeline are all documented in [architecture.md](architecture.md).

## Further reading

- `[design.md](design.md)`: why the system is shaped this way: the dialectic framing, the information-bottleneck restatement, why the Critic is memoryless, the cross-round profile, trade-offs, known issues, and the references behind each choice.
- `[architecture.md](architecture.md)`: module-level overview, control flow, and the seams to extend.

## License

[MIT](LICENSE).
