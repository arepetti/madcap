# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **Rewritten in C# / .NET.** The debate algorithm is preserved exactly; only the runtime and infrastructure changed. The original Python implementation is moved verbatim to `old-python-src/`.
- **Local backend is now [Microsoft Foundry Local](https://learn.microsoft.com/azure/foundry-local/) instead of Ollama**, integrated through `Microsoft.Extensions.AI`'s `IChatClient`. A generic OpenAI-compatible remote backend is selectable via configuration (`Debate:Provider`), with no algorithm changes.
- **New model defaults** (three distinct families): `phi-4` (Answerer), `mistral-7b-v0.2` (Critic), `qwen3-8b` (Judge), defined in one place (`appsettings.json`).
- **Models preload in the background;** the REPL no longer blocks on model loading. 
- **The Judge is split into four single-job contexts.** Instead of one Judge actor doing everything in a shared buffer, the rephrase, restate, verdict, and profile jobs each get their own conversation buffer (all routed to the Judge model) so they no longer share context they shouldn't: the **rephraser** never sees the debate, the **restater** is the only context that ingests raw Answerer text, the **arbiter** rules on the debate in rephrased form only (restatements + objections, no raw Answerer responses), and the **profiler**'s only input is the Critic's objections. The rephraser additionally remembers prior rephrased questions paired with their verdicts so follow-up questions stay consistent. Each Judge sub-role has its own persona file (`<preset>.judge-rephraser|judge-restater|judge-arbiter|judge-profiler.txt`).
- **Actors now exchange JSON, not magic strings.** Each phase sends a prompt that states the exact JSON shape and parses a typed reply (`JsonProtocol`/`DebatePrompts`), replacing the fragile `REPHRASED:`/`CLARIFY:`/`PROFILE_NOTE:`/"no further objections"/regex-confidence protocol. Parsing is tolerant (strips code fences, isolates the `{...}` span) and re-asks once on failure.

### Added

- **Unit test project (`Debate.Tests`, xUnit).** Covers the JSON contract and tolerant parsing (`<think>` stripping, code fences, flattening wrong-typed fields, raw-newline rescue), prompt building, confidence parsing, the profile merge/surface policy, persona resolution, and — via scripted in-memory model clients — the full pipeline ping/pong: rephrase → debate → verdict → profile, the Answerer clarification path (skips the Critic), the round cap, and parse/empty-answer recovery. Several tests double as executable checks of the design.md context-isolation invariants (only the restater sees raw Answerer text; the arbiter rules on rephrased form only; the profiler sees only objections; the rephraser never sees the debate). The maximum number of rounds per question is now a setting.
- **The Answerer can ask for missing information instead of guessing.** On its first turn it may reply with a clarifying question; the Judge rephraser relays it to the user and rephrases the user's reply into neutral facts that are fed back to the Answerer. This pre-debate clarification skips the Critic entirely and does not count as a debate round (bounded to avoid loops).
- `!context` **command** showing exactly what each actor receives: its rendered system prompt, current conversation buffer, and the relevant per-phase prompt templates.

## [0.1.0] - 2026-05-18

### Added

- Three-agent debate loop: Answerer, Critic, Judge with structured rounds.
- Judge rephrase (bias-dampening), plain-fact restatement, and Answerer profiling.
- Interactive CLI with `!stats`, `!new`, and session commands.
- Persona system with per-role `.txt` files and named presets.
- Windows *installer* (`install.ps1`): Ollama setup, model pulls with retry logic, Python venv creation.

