# Datasets

From Wynn, A., Satija, H., & Hadfield, G. (2025). *Talk Isn't Always Cheap: Understanding Failure Modes in Multi-Agent Debate.* arXiv:2509.05396.

**CommonSenseQA**: The CommonSenseQA dataset (Talmor et al., 2019) consist of multiple-choice questions with complex semantics that often require prior knowledge to answer correctly. The dataset is intended to test for prior common-sense knowledge encoded within LLMs and check for common misconceptions.

**MMLU**: Massive Multitask Language Understanding, or MMLU (Hendrycks et al., 2021), is a widely used multiple-choice dataset covering 57 domains including elementary mathematics, US history, computer science, law, and more. To perform well on MMLU, models need robust world knowledge and problem solving ability.

**GSM8K**: GSM8K (Cobbe et al., 2021) is a dataset of linguistically diverse grade school math word problems which require multi-step mathematical reasoning to solve. This dataset is not multiple-choice and instead requires open-ended generation of the answer to the math questions, potentially with intermediate reasoning steps.

# Prompts

From Wynn, A., Satija, H., & Hadfield, G. (2025). *Talk Isn't Always Cheap: Understanding Failure Modes in Multi-Agent Debate.* arXiv:2509.05396.

**CommonSenseQA**: Can you answer the following question as accurately as possible? If a product doesn’t last, what does it have a reputation of doing?: A) disintegrate, B) wear out, C) desolved, D) fall apart, E) dissipate Explain your answer by providing a bullet point summary of your reasoning, putting the answer in the form (X) at the end of your response.

**MMLU**: Can you answer the following question as accurately as possible? What is the value of p in 24 = 2p?: A) p = 4, B) p = 8, C) p = 12, D) p = 24 Explain your answer by providing a bullet point summary of your reasoning, putting the answer in the form (X) at the end of your response.

**GSM8K**: Can you solve the following math problem? Mark is trying to choose between two venues for a surprise party for his wife. The first venue charges a flat fee of $200, regardless of how many guests attend. While the second charges, $25 per person who attends. However, the first venue does not include food, which Mark estimates will cost $5 for each person who attends. At the second venue, food for each guest is already included in the price. How many guests are necessary for the two venues to be equal in cost? Provide a bullet point summary of your reasoning. Your final answer should be a single numerical number, in the form *answer*, at the end of your response.

# Reading `!stats` for evaluation

To be clear about what exists: this file describes a protocol to follow by hand. There is no evaluation harness in this repository — no dataset loader, no scorer, no batch runner — and none of the comparisons below have been run. `!stats` measures cost and context usage only; nothing it reports correlates with whether a verdict was correct. Treat every claim in [design.md](design.md) about the debate improving answers as an untested hypothesis until this protocol is actually executed.

The datasets above are the *what* of evaluation and the prompts are the *how you ask*. `!stats` is the *what it cost*. Used together with an external output-quality check (did the verdict actually match the dataset's ground-truth answer?), these numbers are what tells you whether this multi-agent debate (DAB, "debate among agents baseline") is doing better, worse, or just *differently* than simpler baselines (a single-shot CoT floor and a Self-Refine style self-critique loop in the spirit of Madaan et al., 2023), and whether one DAB configuration is worth more than another. The session counters reset on `!new`.

## What the numbers mean

`!stats` prints session totals in the order below. Read them in pairs (counters, then wall time, then tokens).

- **Questions / Clarifications.** *Questions* counts user prompts that reached the pipeline. *Clarifications* counts the times the Judge rephraser replied `{"action":"clarify"}` instead of `{"action":"rephrase"}`, plus the times the Answerer asked the user for missing information before its first turn. A high clarification-to-question ratio means the persona is leaving too much room for the Judge to push back. Useful when comparing presets, less useful for raw cost.
- **Debate rounds (total).** Cumulative debate iterations across the session, capped per question by `SessionConfig.MaxRounds` (set from `Debate:Defaults:MaxRounds` in [src/Debate.Cli/appsettings.json](src/Debate.Cli/appsettings.json), the setup wizard, or `--rounds`). Always hitting the cap means the Critic never says "no further objections" (push back on Critic temperature or persona). Always exactly one means the Critic is giving up immediately (the opposite problem).
- **Wall time, `question -> answer` (total and last).** Includes Phase 1 clarification round-trips. This is the *user-experienced* latency.
- **Wall time, `rephrased -> answer` (total and last).** Strips out the clarification dialogue. This is the *debate-only* latency, and is the right number to compare against a single-shot or Self-Refine baseline that has no clarification step.
- **Tokens, `total`.** Headline cost number. Sum of the five buckets below.
- **Tokens, `rephrase question`.** All Phase 1 traffic (initial question, Judge replies, nudges, user clarifications). Self-Refine has no equivalent. A large value usually means clarification rather than the rephrase itself.
- **Tokens, `answerer turns`.** Every `Answerer.send` call in Phase 2 (the rephrased question, every critique fed back in, every Answerer reply). Closest counterpart to a Self-Refine "generate" call summed across revisions.
- **Tokens, `critic (restate + critic)`.** Judge restatements plus Critic critiques. The restate cost is the price of the channel constraint described in [design.md](design.md) ("Restatement as an extractive paraphrase"). The Critic cost is the equivalent of Self-Refine's "critique" call.
- **Tokens, `judge verdict`.** Single call per question. It scales with the number of debate rounds — the transcript handed to the arbiter carries one restatement/objection pair per round — but not with session length: the arbiter's system prompt is the persona file alone, with no session history rendered into it. So a verdict cost that climbs across a session means rounds are climbing, not that context is leaking in.
- **Tokens, `profile (phase 3 + render)`.** The Judge's Phase 3 extraction call plus the rendered profile snippet propagated into the Critic's system prompt each round. Collapses to zero when profile-building is off; that on/off flag is visible in `!stats` immediately above this table.

## Comparing DAB against baselines

Concrete protocol:

1. Pick a benchmark prompt from the `# Prompts` section above (CommonSenseQA / MMLU / GSM8K).
2. Run it through DAB, then `!stats`. Record from the table: `tokens total`, `wall time rephrased -> answer (last)`, the verdict text, the verdict confidence label (`low`, `medium`, `high`), and a correctness flag (extracted with a deterministic rule, see below).
3. Run the same prompt through a Self-Refine baseline (single model, `generate -> critique -> refine` loop in the spirit of Madaan et al., 2023). Record the same fields.
4. Run the same prompt through a single-shot CoT baseline (one model, one pass, no critique, plain chain-of-thought; see *Things to try next* in [design.md](design.md) for the planned in-CLI mode). Record the same fields. This is the *floor* that tells you whether any critique loop is helping at all: without it, a DAB-vs-Self-Refine delta cannot separate "MAD helps" from "any critique loop helps".

Repeat each prompt under each system at least `k = 3` times with different decoding seeds, and run at least `n = 50` distinct prompts per configuration.

Extract correctness with a deterministic regex rather than by hand: CSQA and MMLU are exact-letter match (the prompts force the final answer into `(X)` form); GSM8K is exact-integer match (the prompt forces `_answer_` delimiters). Fall back to a hand-check only when the extractor finds no match, and log those misses as a separate "extraction miss" rate per configuration — a high rate is itself a signal that the persona is producing answers in a form the protocol can't score.

Headline comparison metrics:

- **Cost per correct answer** = `tokens_total / (correct / n_runs)`. The lower the better. This is what tells you whether DAB's extra structure is worth its extra cost. Report a bootstrapped 95 % confidence interval over per-prompt resamples; a single point estimate at `n = 50` is still noisy.
- **Latency per correct answer** = `wall_time_post_rephrase / questions`, averaged across runs at fixed temperature seeds. Comparable across all three systems because none of these numbers include a Phase 1 clarification dialogue (CoT and Self-Refine have none; DAB's is excluded by construction here).
- **Raw accuracy** with a Wilson 95 % confidence interval is the other axis. Report it separately so the trade-off is visible.
- **Paired prompt-level differences.** Hold the prompt set fixed across all three systems and report the *paired* difference in correctness and cost per prompt rather than independent-sample means. With the same `n` the paired comparison has materially more power, and the CSV schema below supports it through the `role` label (one row per system per prompt batch).

DAB's `rephrase` and `profile` token buckets have no equivalent in Self-Refine or CoT. When you sum tokens for the comparison, decide upfront whether to (a) include them, which is fair to DAB's design and treats clarification as part of the offering, or (b) exclude them, which steel-mans the baselines by counting only directly comparable work. If in doubt, report both.

## Comparing DAB configurations

Hold the prompt set fixed, vary one knob at a time, re-run. What to watch in `!stats`:

- **Persona preset** ([src/personas/](src/personas), or `--persona`). Moves `clarifications`, `debate rounds`, `tokens critic`. Note that `default` and `technical` differ in the *content* of each role's brief but share the same structure and the same JSON output rules, so this is a lighter knob than model choice or temperature.
- **Per-role temperatures** (`Debate:Defaults:{Answerer,Critic,Judge}Temp` in [src/Debate.Cli/appsettings.json](src/Debate.Cli/appsettings.json), or the setup wizard). Critic temperature drives `debate rounds` and `tokens critic`; Answerer temperature drives `tokens answerer`; Judge temperature is mostly invisible until it starts hurting `rephrase` or `verdict`.
- **Models.** For the local backend, switch the whole lineup with `--profile <name>` (`normal`, `small`, `smaller`) or edit `Debate:FoundryLocal:Profiles` directly; for the remote backend, edit `Debate:Remote:Models`. Moves `tokens total` and both wall-time numbers. Accuracy is the orthogonal axis.
- **Execution mode** (`--execution-mode`, or the profile's `ExecutionMode`). Pure latency/memory knob: `Sequential` reloads models as roles change, which inflates wall time without touching token counts. Hold it fixed when comparing anything else.
- **Profile building on/off** (flag shown above the stats table). `tokens profile` collapses to zero. Compare accuracy with and without to validate the profile is doing useful work.
- **Round cap** (`--rounds`, or `Debate:Defaults:MaxRounds`). Directly bounds `debate rounds` and indirectly bounds `tokens critic` and `tokens answerer`.

## Exporting `!stats` to CSV

Use `!stats export <path>` to append the current session's totals to a CSV file, or `!stats export` to be prompted for a path. The command then asks for a `role` label (default: the active persona preset name, e.g. `default` or `technical`). Use this label to tell test setups apart in subsequent comparisons; any short string works (`gpt4o-baseline`, `technical-low-temp`, `profile-off`, ...).

The file is append-only. The header row is written once, when the file is first created; later exports add a single data row. Columns, in order: `role, questions, clarifications, debate_rounds, wall_time_total, last_wall_time_total, wall_time_post_rephrase, last_wall_time_post_rephrase, tokens_total, tokens_rephrase, tokens_answerer, tokens_critic, tokens_verdict, tokens_profile, verdict_confidence_low, verdict_confidence_medium, verdict_confidence_high`. The three `verdict_confidence_*` counters are session totals of how many verdicts came back at each label; together with an external correctness column they let you check whether `high` actually means anything across a session (the calibration question raised under *Calibrated confidence* in [design.md](design.md)).

Typical workflow for a comparison run: pick a fixed CSV path, run the prompts from `# Prompts` under one configuration, `!stats export` with a descriptive role label, then `!new` and switch knobs (persona, temperatures, models, round cap, profile on/off) for the next row.

## Caveats

- Token counts come from a local cl100k_base tokenizer ([src/Debate.Core/TiktokenCounter.cs](src/Debate.Core/TiktokenCounter.cs), via `Microsoft.ML.Tokenizers`, with a character-ratio fallback if the encoder is unavailable). That is OpenAI's tokenizer, so for Phi/Mistral/Qwen models the counts are a consistent proxy rather than the exact tokens those models see. They count the request payload plus the assistant reply for each call, and do **not** include the system-prompt and conversation-history overhead the model also processes on every turn. The `!stats` per-actor table covers that separately as "context fill" if you need it.
- Wall times are local clock time. Network latency to a hosted API, queue time inside the model, and terminal-rendering time are all included. Compare runs only within the same environment.
- A single question's numbers are noisy. The 50-prompt / 3-seed floor in the protocol above is the minimum honest sample size for the cost-per-correct-answer metric, and even at that `n` a paired prompt-level comparison is preferred over independent-sample means.
- MMLU has known label noise of roughly 5–10 % in some subjects. At small `n` this is indistinguishable from a real configuration-level effect, so do not read a 2–3 pp accuracy delta on MMLU as significant.
- The default Foundry Local backend runs 3–14B models depending on the profile (`smaller` is 3–4B throughout; `small` and `normal` are larger), all materially smaller than the GPT-4-class debaters and judges used in the MAD literature cited in [design.md](design.md) (see *Default models are smaller than the literature assumes* in *Known issues*). Numbers from this CLI on the defaults are useful for *within-configuration* comparisons but should not be quoted against external MAD-vs-baseline results without re-validating on a stronger backend first.
- Under `auto` execution-provider resolution, a model alias resolves to a hardware-specific variant against the live catalog at startup, so the same profile name can mean different weights on different machines. Pin the resolved variant (visible in the model host's startup log) before quoting any number as reproducible.

For *why* each of these phases exists in the first place and what failure mode each one is meant to neutralise, see [design.md](design.md).