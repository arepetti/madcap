# Multi-Agent Debate

**MADJURY** (Multi-Agent Debate with a Judge that Unbiases, Restates, and Yields the verdict) is an interactive CLI that answers your questions by running a small structured debate between three LLMs instead of asking one model and trusting the reply. A confident **Answerer** drafts a response, a sceptical **Critic** stress-tests it, and an impartial **Judge** mediates the exchange and writes the final verdict. The user sees the rephrased question, every round of debate, and the verdict with a confidence label. For benchmark prompts and how to test, evaluate, and compare configurations via `!stats`, see [testing.md](testing.md).

The Judge does extra work that is the point of the system: it rewrites the user's question into a neutral form before anyone sees it (to dampen prompt-side bias), restates every Answerer turn as plain facts before the Critic ever sees them (so the Critic argues with the *claim*, not the prose), and quietly builds a profile of the Answerer's recurring weaknesses across rounds so future Critics know where to probe harder. The full design rationale, references and known issues live in [design.md](design.md); the code map and extension points are in [architecture.md](architecture.md).

This is a toy project for experimenting with multi-agent debate protocols, not a production system.

This is the C# / .NET rewrite. It runs models locally on [Microsoft Foundry Local](https://learn.microsoft.com/azure/foundry-local/) by default and can switch to any OpenAI-compatible remote endpoint via configuration. The original Python implementation (which used Ollama) is preserved unchanged under [old-python-src/](old-python-src). The debate *algorithm* is identical; only the runtime, the model lineup, and the infrastructure changed.

Fair warning: this is a work-in-progress. Changes get pushed to the repo whenever something interesting occurs to me, and they might break existing functionality without notice. The latest version might not even work at all. This is a playground for experimentation, not a stable library. Treat it accordingly.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) on `PATH`.
- [Foundry Local](https://learn.microsoft.com/azure/foundry-local/get-started) for the default local backend:
  - Windows: `winget install Microsoft.FoundryLocal`
  - macOS: `brew install foundrylocal`
- Several GB of free disk and RAM **per model**. The default `small` profile uses three ~2–4 GB models (`phi-4-mini`, `ministral-3-3b-instruct-2512`, `qwen3-4b`); the `normal` profile uses larger ones (`phi-4`, `mistral-7b-v0.2`, `qwen3-8b`, roughly 5–8 GB each). They download on first run.

The remote backend needs no local models, just an endpoint and an API key (see [Switching to a remote backend](#switching-to-a-remote-backend)).

### Automated setup (Windows)

On Windows you can do all of the above in one step. From the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1
```

It verifies/installs the .NET SDK and Foundry Local (via `winget`), builds the solution, checks the persona files, and offers to download the configured models immediately so the first real run is fast. Pass `-Yes` for an unattended run or `-SkipBuild` to skip the build.

## Quick start

From the repo root:

```bash
cd src
dotnet run --project Debate.Cli
```

The first run with the local backend downloads the configured models (this can take a while); progress is shown. After that, startup is fast. You'll get a short setup wizard (persona, temperatures, profiling) and then the debate prompt.

## Using it

Type a question and watch the round play out. A few built-in commands are available at the prompt:

| Command     | What it does                                                    |
| ----------- | --------------------------------------------------------------- |
| `!help`     | List commands.                                                  |
| `!new`      | Start a fresh session (wipe Answerer memory, history, profile). |
| `!personas` | Show role -> model -> temperature -> persona file.              |
| `!context`  | Show exactly what each actor receives: rendered system prompt, conversation buffer, and per-phase prompt templates. |
| `!stats`    | Per-actor context-window usage, profile state, history. `!stats export [path]` appends a CSV row. |
| `<empty>`   | Exit.                                                           |

Anything that does not start with `!` is treated as a question for the debate.

### Command-line options

| Option            | Effect                                                                 |
| ----------------- | ---------------------------------------------------------------------- |
| `--provider`      | `FoundryLocal` (default) or `Remote`. Overrides config.                |
| `--profile`       | Foundry Local model profile, e.g. `small` (default) or `normal`. Overrides config. |
| `--execution-provider` | Force a provider: `auto` (default), `cpu`, `cuda`, `webgpu`. Use `cpu` to avoid GPU out-of-memory. |
| `--execution-mode` | Model residency for the active profile: `parallel` (all loaded, fast), `sequential` (one at a time, lowest memory), or `semisequential` (Judge stays resident, others cycle). |
| `--no-window`     | Launch per-model host processes with no console window (default: with a window). |
| `--persona`       | Persona preset to use; also skips the persona prompt in the wizard.    |
| `--persona-dir`   | Directory containing persona `.txt` files.                             |
| `--no-wizard`     | Skip the interactive setup and use the configured defaults.            |

```bash
dotnet run --project Debate.Cli -- --provider Remote --no-wizard
```

## Configuration

All settings live in [src/Debate.Cli/appsettings.json](src/Debate.Cli/appsettings.json) and bind via the standard .NET options pattern. The shape:

```jsonc
{
  "Debate": {
    "Provider": "FoundryLocal",          // or "Remote"
    "Defaults": { "Persona": "default", "AnswererTemp": 0.3, "CriticTemp": 0.9, "JudgeTemp": 0.3, "BuildProfile": true },
    "FoundryLocal": {
      "ContextSize": 8192,
      "ExecutionProvider": "auto",        // top-level override; "auto" defers to the profile's setting
      "SeparateWindows": true,            // per-model host processes get a console window (false = --no-window)
      "Profile": "small",                 // active model profile (override with --profile)
      "Profiles": {
        "normal": { "Answerer": "phi-4",      "Critic": "mistral-7b-v0.2",             "Judge": "qwen3-8b", "ExecutionProvider": "cuda", "ExecutionMode": "Parallel" },
        "small":  { "Answerer": "phi-4-mini", "Critic": "ministral-3-3b-instruct-2512", "Judge": "qwen3-4b", "ExecutionProvider": "cuda", "ExecutionMode": "SemiSequential" }
      }
    },
    "Remote": {
      "Endpoint": "https://api.openai.com/v1",
      "ApiKeyEnvVar": "DEBATE_API_KEY",
      "ContextSize": 128000,
      "Models": { "Answerer": "gpt-4o-mini", "Critic": "gpt-4o-mini", "Judge": "gpt-4o" }
    }
  }
}
```

### Model lineup and profiles

Models are organized into named **profiles** — each a complete role lineup — so you can trade quality for resource use without editing individual aliases. Pick one with `--profile <name>` or the `Profile` config key (default `small`). Every profile uses **three distinct model families**, which satisfies both design invariants at once (see [design.md](design.md)): the Critic differs from the Answerer to avoid an echo chamber, and the Judge differs from the Answerer to avoid self-preference bias when it restates and judges.

| Role     | `small` (default)               | `normal`            | Why                                                                 |
| -------- | ------------------------------- | ------------------- | ------------------------------------------------------------------- |
| Answerer | `phi-4-mini`                    | `phi-4`             | Drafts the substantive answer (Phi family).                          |
| Judge    | `qwen3-4b`                      | `qwen3-8b`          | Strong instruction-following for the single-task JSON prompts (rephrase/clarify, restate, verdict) (Qwen family). |
| Critic   | `ministral-3-3b-instruct-2512`  | `mistral-7b-v0.2`   | A third family, run hot for diverse objections (Mistral family).     |

The `small` profile keeps every model around 2–4 GB for modest hardware; `normal` uses the larger, higher-quality models. Each profile binds its lineup to a device and a residency strategy:

- `ExecutionProvider` — `auto`, `cpu`, `cuda`, or `webgpu`. Both default profiles use **`cuda`**. The top-level `ExecutionProvider` (and `--execution-provider`) overrides the profile when set to a concrete value; `auto` defers to the profile.
- `ExecutionMode` — how per-model processes are kept resident:
  - **`Parallel`** (the `normal` default) keeps every model resident for speed.
  - **`Sequential`** keeps only one model in memory at a time, terminating the others before loading the next, so even three GPU models never coexist in VRAM (no out-of-memory) at the cost of a reload when the active role changes.
  - **`SemiSequential`** (the `small` default) keeps the Judge resident while the Answerer and Critic cycle one at a time (peak memory = Judge + one other). Since the Judge runs between every step (rephrase, restatement, verdict, profile), this avoids the constant Judge reloads of `Sequential` while still bounding memory — a good middle ground.

  Override per run with `--execution-mode`.

#### Per-model process pool

The Foundry Local backend runs **each role's model in its own child process** of the same `debate` executable (re-invoked with an internal `__serve-model` argument), communicating over a line-delimited JSON protocol on stdin/stdout. "Unloading" a model in `Sequential` mode means terminating its process, which fully releases its RAM/VRAM — the reliable way to free GPU memory between roles. Each child loads exactly one model variant and serves it; its load progress is relayed to the main console via stderr. The `SeparateWindows` setting (default `true`, or `--no-window` to disable) controls whether those child processes are created with a console window; stdio is always redirected for the protocol regardless.

These lineups are defined in exactly one place: [src/Debate.Cli/appsettings.json](src/Debate.Cli/appsettings.json) (the `scripts/install.ps1` setup reads the active profile from there). Catalog aliases occasionally change and resolve to hardware-specific variants. Under `auto`, each model host resolves its alias against the live catalog at startup, **prefers an already-cached compatible variant, then the most capable GPU variant whose execution provider is registered, with CPU as a guaranteed fallback** — variant selection does not account for VRAM capacity, so prefer `Sequential` (or a lighter profile) when models are large. It downloads a variant if missing and fails fast with a clear message if an alias is unavailable. Browse available models at [foundrylocal.ai/models](https://www.foundrylocal.ai/models) or run `foundry model list`.

### Switching to a remote backend

Set the provider to `Remote` (in config or via `--provider Remote`), point `Endpoint` at any OpenAI-compatible API, and provide the key through the environment variable named by `ApiKeyEnvVar`:

```bash
$env:DEBATE_API_KEY = "sk-..."   # PowerShell;  export DEBATE_API_KEY=sk-... on bash
dotnet run --project Debate.Cli -- --provider Remote
```

The debate algorithm is unaware of which backend is in use; both are injected behind the same `IChatClient` seam.

## Extending and configuring

Adding commands, swapping models, switching local/remote, adding personas, and tweaking the debate pipeline are documented in [architecture.md](architecture.md).

## Further reading

- [design.md](design.md): why the system is shaped this way: the dialectic framing, the channel-constraint restatement, why the Critic is memoryless, the cross-round profile, trade-offs, known issues, and references.
- [architecture.md](architecture.md): the .NET solution layout, control flow, the `IChatClient`/provider seam, and the host abstractions.
- [old-python-src/](old-python-src): the original Python (Ollama-based) implementation.

## License

[MIT](LICENSE).
