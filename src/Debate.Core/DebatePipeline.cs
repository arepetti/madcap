using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Debate.Core.Actors;

namespace Debate.Core;

/// <summary>
/// The per-question state machine: rephrase (loop on rephrase/clarify), debate
/// (Judge restates each Answerer turn as neutral facts before the Critic sees it;
/// the Answerer hears critiques raw; the Judge issues a verdict over an interleaved
/// transcript), then bookkeeping and profile-note extraction.
///
/// Every exchange with an actor is a JSON contract: the pipeline sends a prompt
/// that states the exact JSON shape (see <see cref="DebatePrompts"/>) and parses a
/// typed reply (see <see cref="JsonProtocol"/>). There are no magic strings in the
/// model output; a reply that is not parseable triggers one automatic re-ask.
///
/// The actors stay dumb. To change the process, change this class or subclass it and
/// override one of the <c>protected virtual</c> phase methods, then pass a factory to
/// <see cref="DebateEngine"/>; the actors and the host need no edits.
/// </summary>
public partial class DebatePipeline
{
    // How many times the Judge is nudged to produce a valid rephrase/clarify reply
    // before the question is aborted.
    public const int MaxRephraseNudges = 3;

    // How many times the Answerer may ask the user (via the rephraser) for missing
    // information before it must commit to an answer. Bounds the pre-debate clarify loop.
    public const int MaxAnswererClarifications = 2;

    // JSON shapes echoed back to an actor when its reply could not be parsed.
    private const string RephraseShape = "{\"action\":\"rephrase\"|\"clarify\",\"text\":\"...\"}";
    private const string AnswerShape = "{\"answer\":\"...\"}";
    private const string RestateShape = "{\"restatement\":\"...\",\"unsupported\":[\"...\"]}";
    private const string CritiqueShape = "{\"scratch\":\"...\",\"done\":false,\"objection\":\"...\"}";
    private const string VerdictShape =
        "{\"answer\":\"...\",\"confidence\":\"low|medium|high\",\"justification\":\"...\",\"uncertainty\":\"...\"}";
    private const string ProfileShape = "{\"tendency\":\"...\"|null}";

    // Shown in place of the transcript if the debate produced no rounds at all.
    private const string EmptyTranscript = "(the Critic raised no objections)";

    private static readonly HashSet<string> NoneNotes = new(StringComparer.Ordinal)
    {
        "none", "no", "n/a", "nothing",
    };

    [GeneratedRegex(@"\b(low|medium|high)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ConfidenceLabelRegex();

    // Protected so a subclass overriding a phase can reach the same collaborators the
    // built-in phases use.
    protected readonly DebateContext _context;
    protected readonly IDebateObserver _observer;
    protected readonly IClarificationSource _clarifications;
    protected readonly ITokenCounter _tokens;

    public DebatePipeline(
        DebateContext context,
        IDebateObserver observer,
        IClarificationSource clarifications,
        ITokenCounter tokens)
    {
        _context = context;
        _observer = observer;
        _clarifications = clarifications;
        _tokens = tokens;
    }

    public virtual async Task RunAsync(string userQuestion, CancellationToken cancellationToken)
    {
        ResetPerQuestion();

        var stats = _context.Stats;
        stats.Questions++;
        long t0 = Stopwatch.GetTimestamp();
        long? t1 = null;
        try
        {
            var rephrased = await RephraseAsync(userQuestion, cancellationToken).ConfigureAwait(false);
            if (rephrased is null)
            {
                return;
            }

            t1 = Stopwatch.GetTimestamp();
            var outcome = await DebateAsync(rephrased, cancellationToken).ConfigureAwait(false);
            if (outcome is null)
            {
                // The Answerer never produced a usable answer; there is nothing to debate,
                // record, or profile. (DebateAsync already warned.)
                return;
            }

            // Bookkeeping: the rephraser keeps the rephrased question and the verdict it
            // reached (the Critic still only reads PriorRephrased; the verdict is shown to
            // the rephraser alone, for follow-up continuity).
            _context.RecordExchange(rephrased, outcome.Value.VerdictText);
            await ExtractProfileNoteAsync(outcome.Value.Objections, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            long tEnd = Stopwatch.GetTimestamp();
            double dtTotal = Stopwatch.GetElapsedTime(t0, tEnd).TotalSeconds;
            stats.WallTimeTotal += dtTotal;
            stats.LastWallTimeTotal = dtTotal;
            if (t1 is not null)
            {
                double dtPost = Stopwatch.GetElapsedTime(t1.Value, tEnd).TotalSeconds;
                stats.WallTimePostRephrase += dtPost;
                stats.LastWallTimePostRephrase = dtPost;
            }
        }
    }

    // Phase 0

    /// <summary>
    /// Rebuilds the Critic and the four Judge contexts so their system prompts pick up
    /// the latest session state (profile, prior exchanges). This runs once per question,
    /// not once per debate round: within a question the Critic keeps the buffer it was
    /// given, which is what makes it memoryless across questions but coherent within one.
    /// </summary>
    protected virtual void ResetPerQuestion()
    {
        _context.Critic.Invalidate();
        foreach (var judge in _context.JudgeContexts())
        {
            judge.Invalidate();
        }
    }

    // Phase 1

    protected virtual async Task<string?> RephraseAsync(string userQuestion, CancellationToken cancellationToken)
    {
        var judge = _context.JudgeRephraser;

        _observer.OnStatus("Asking the Judge to interpret and rephrase your question...");
        var prompt = DebatePrompts.BuildRephrase(userQuestion);

        // SendJsonAsync already re-asks once on a parse failure, so we do NOT stack a
        // second JSON-retry loop here (that produced a storm of duplicate Judge calls).
        // This loop only advances the legitimate clarify -> user reply -> rephrase
        // cycle, plus a single bounded nudge when the Judge parses but picks neither
        // action.
        int nudges = 0;
        while (true)
        {
            var (reply, _) = await SendJsonAsync<JudgeRephraseReply>(
                judge, prompt, RephraseShape, TokenBucket.Rephrase, cancellationToken).ConfigureAwait(false);

            if (reply is null)
            {
                // Already re-asked once inside SendJsonAsync; don't keep hammering.
                _observer.OnWarning(
                    "judge did not return a usable rephrase/clarify reply (even after a re-ask) — " +
                    "aborting this question");
                return null;
            }

            if (reply.IsRephrase && !string.IsNullOrWhiteSpace(reply.Text))
            {
                var rephrased = reply.Text.Trim();
                _observer.OnRephrased(rephrased);
                return rephrased;
            }

            if (reply.IsClarify && !string.IsNullOrWhiteSpace(reply.Text))
            {
                var next = await RequestRephraseClarificationAsync(reply.Text.Trim(), cancellationToken)
                    .ConfigureAwait(false);
                if (next is null)
                {
                    return null;
                }

                prompt = next;
                continue;
            }

            if (!TryNudgeRephrase(userQuestion, ref nudges, out prompt))
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Surfaces the Judge's clarifying question, collects the user's reply, and returns the
    /// follow-up prompt that feeds it back to the rephraser. Returns null to abort the
    /// question (the user cancelled with null or skipped with an empty reply).
    /// </summary>
    private async Task<string?> RequestRephraseClarificationAsync(string question, CancellationToken cancellationToken)
    {
        _context.Stats.Clarifications++;
        _observer.OnClarify(question);

        var userReply = await _clarifications
            .RequestClarificationAsync(question, cancellationToken)
            .ConfigureAwait(false);

        if (userReply is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(userReply))
        {
            _observer.OnWarning("clarification skipped — aborting this question");
            return null;
        }

        _observer.OnStatus("Sending your clarification to the Judge...");
        return DebatePrompts.BuildClarifyFollowUp(userReply);
    }

    /// <summary>
    /// Handles a rephrase reply that parsed cleanly but chose neither action: emits a
    /// bounded nudge and yields the next (no-think) prompt, or returns false once the nudge
    /// budget is spent (the caller then aborts the question).
    /// </summary>
    private bool TryNudgeRephrase(string userQuestion, ref int nudges, out string prompt)
    {
        if (nudges >= MaxRephraseNudges)
        {
            _observer.OnWarning(
                $"judge would not choose rephrase or clarify after {MaxRephraseNudges} nudges — " +
                "aborting this question");
            prompt = string.Empty;
            return false;
        }

        nudges++;
        _observer.OnWarning(
            $"judge reply chose neither 'rephrase' nor 'clarify'; nudging ({nudges}/{MaxRephraseNudges})");
        _observer.OnStatus("Re-asking the Judge to rephrase or clarify...");
        prompt = DebatePrompts.WithNoThink(
            DebatePrompts.BuildRephrase(userQuestion), ModelFor(DebateRole.Judge));
        return true;
    }

    // Phase 2

    protected virtual async Task<DebateOutcome?> DebateAsync(string rephrasedQuestion, CancellationToken cancellationToken)
    {
        // The Answerer may ask the user (via the rephraser) for missing information before
        // its first turn; that exchange does not count as a round and never reaches the
        // Critic. A null answer means there is nothing to debate.
        var answererReply = await GetInitialAnswerAsync(rephrasedQuestion, cancellationToken)
            .ConfigureAwait(false);
        if (answererReply is null)
        {
            return null;
        }

        _observer.OnAnswerer(answererReply);

        var (rounds, objections) = await RunDebateRoundsAsync(answererReply, cancellationToken)
            .ConfigureAwait(false);
        var verdictText = await IssueVerdictAsync(rounds, cancellationToken).ConfigureAwait(false);
        return new DebateOutcome(verdictText, objections);
    }

    /// <summary>
    /// Drives the round loop (restate → critique → rebuttal) up to the configured maximum,
    /// stopping early when the Critic is done or an actor falls silent. Returns the
    /// per-round restatement/objection records (for the verdict transcript) and the raw
    /// objections (the profiler's only input).
    /// </summary>
    protected virtual async Task<(List<RoundRecord> Rounds, List<string> Objections)> RunDebateRoundsAsync(
        string answererReply, CancellationToken cancellationToken)
    {
        int maxRounds = _context.Config.MaxRounds;
        var rounds = new List<RoundRecord>();
        var objections = new List<string>();

        for (int round = 0; round < maxRounds; round++)
        {
            _context.Stats.DebateRounds++;
            AccountForProfileTokens();

            var restatement = await RestateAsync(answererReply, round, maxRounds, cancellationToken)
                .ConfigureAwait(false);

            var (done, objection) = await GetObjectionAsync(restatement, round, maxRounds, cancellationToken)
                .ConfigureAwait(false);
            if (done || string.IsNullOrEmpty(objection))
            {
                rounds.Add(new RoundRecord(restatement, "(no further objections)"));
                break;
            }

            _observer.OnCritic(objection);
            objections.Add(objection);
            rounds.Add(new RoundRecord(restatement, objection));

            var rebuttal = await GetRebuttalAsync(objection, round, maxRounds, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(rebuttal))
            {
                // No rebuttal to restate; stop rather than feed an empty turn into the next
                // round's restatement (which would restate nothing).
                _observer.OnWarning("Answerer returned an empty rebuttal; ending the debate early.");
                break;
            }

            answererReply = rebuttal;
            _observer.OnAnswerer(answererReply);
        }

        return (rounds, objections);
    }

    // The profile snippet is rebuilt into the Critic's system prompt every round; count it
    // here so its cost is visible separately in stats.
    private void AccountForProfileTokens() =>
        _context.Stats.Add(
            TokenBucket.Profile,
            CountTokens(PersonaLibrary.RenderAnswererProfile(_context.ActiveProfile())));

    /// <summary>
    /// The channel constraint: the restater re-expresses the Answerer's reply as neutral
    /// facts. It is the only Judge context that ever sees the raw reply; the Critic and the
    /// verdict only ever see this restatement. Falls back to the raw answer if it fails.
    /// </summary>
    protected virtual async Task<string> RestateAsync(
        string answererReply, int round, int maxRounds, CancellationToken cancellationToken)
    {
        _observer.OnStatus($"Asking the Judge to restate the answer as neutral facts (round {round + 1}/{maxRounds})...");
        var (restated, _) = await SendJsonAsync<JudgeRestatementReply>(
            _context.JudgeRestater, DebatePrompts.BuildRestate(answererReply), RestateShape, TokenBucket.Critic, cancellationToken)
            .ConfigureAwait(false);

        var restatement = restated?.Restatement?.Trim();
        if (string.IsNullOrEmpty(restatement))
        {
            restatement = answererReply;
        }
        else
        {
            restatement = AppendUnsupportedClaims(restatement, restated?.Unsupported);
        }

        _observer.OnRestatement(restatement);
        return restatement;
    }

    /// <summary>
    /// Folds the restater's flagged-unsupported list into the text the Critic receives.
    /// The flag is kept out of the restatement proper (so the restater is not asked to
    /// editorialise text it was told to re-express faithfully) but still has to reach the
    /// Critic, which is the actor that can do something with it.
    /// </summary>
    private static string AppendUnsupportedClaims(string restatement, IReadOnlyList<string>? unsupported)
    {
        var claims = unsupported?
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .ToList();

        if (claims is null || claims.Count == 0)
        {
            return restatement;
        }

        var builder = new StringBuilder(restatement);
        builder.Append("\n\nAsserted without support:");
        foreach (var claim in claims)
        {
            builder.Append("\n- ").Append(claim);
        }

        return builder.ToString();
    }

    protected virtual async Task<(bool Done, string Objection)> GetObjectionAsync(
        string restatement, int round, int maxRounds, CancellationToken cancellationToken)
    {
        _observer.OnStatus($"Asking the Critic to challenge the answer (round {round + 1}/{maxRounds})...");
        var (critReply, _) = await SendJsonAsync<CriticReply>(
            _context.Critic, DebatePrompts.BuildCritique(restatement), CritiqueShape, TokenBucket.Critic, cancellationToken)
            .ConfigureAwait(false);

        bool done = critReply?.Done ?? false;
        var objection = critReply?.Objection?.Trim() ?? string.Empty;
        return (done, objection);
    }

    /// <summary>
    /// The Answerer hears the critique raw and responds. Its rebuttal is fed back into the
    /// next round's restatement, so the verdict sees it as rephrased facts rather than raw
    /// text — keeping the arbiter's context "rephrased only".
    /// </summary>
    protected virtual async Task<string> GetRebuttalAsync(
        string objection, int round, int maxRounds, CancellationToken cancellationToken)
    {
        _observer.OnStatus($"Asking the Answerer to respond to the Critic (round {round + 1}/{maxRounds})...");
        var (response, _) = await SendJsonAsync<AnswererReply>(
            _context.Answerer, DebatePrompts.BuildRespondToObjection(objection), AnswerShape, TokenBucket.Answerer, cancellationToken)
            .ConfigureAwait(false);
        return response?.Answer?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// The arbiter is a fresh Judge context that has never seen the raw Answerer text: it
    /// is handed the debate purely as rephrased facts — the restatements paired with the
    /// Critic's objections, round by round. Records the parsed confidence and surfaces the
    /// verdict, returning its text for bookkeeping.
    /// </summary>
    protected virtual async Task<string> IssueVerdictAsync(
        IReadOnlyList<RoundRecord> rounds, CancellationToken cancellationToken)
    {
        _observer.OnStatus("Asking the Judge to weigh the debate and issue a verdict...");
        var (verdict, verdictRaw) = await SendJsonAsync<JudgeVerdictReply>(
            _context.JudgeArbiter, DebatePrompts.BuildVerdict(FormatDebateTranscript(rounds)), VerdictShape, TokenBucket.Verdict, cancellationToken)
            .ConfigureAwait(false);

        ConfidenceLabel? confidence;
        string verdictText;
        if (verdict is not null)
        {
            confidence = NormalizeConfidence(verdict.Confidence) ?? ParseConfidence(verdictRaw);
            verdictText = ComposeVerdict(verdict);
        }
        else
        {
            // Both the call and the re-ask failed to parse, so the raw reply becomes the
            // user's answer. Strip reasoning first, as Actor.SendAsync does: otherwise a
            // degenerate <think> loop is printed as the verdict and then stored in the
            // rephraser's session memory for the rest of the session.
            confidence = ParseConfidence(verdictRaw);
            verdictText = JsonProtocol.StripReasoning(verdictRaw).Trim();
        }

        _context.Stats.RecordConfidence(confidence);
        _observer.OnVerdict(verdictText, confidence);
        return verdictText;
    }

    /// <summary>
    /// Gets the Answerer's initial answer, allowing it to first ask the user (mediated by
    /// the Judge rephraser) for missing information. A clarification turn does NOT count as
    /// a debate round and never reaches the Critic: the user's reply is rephrased into
    /// neutral facts and fed straight back to the Answerer. Returns the answer text, or
    /// null if the Answerer never produced a usable answer (e.g. the user skipped/aborted).
    /// </summary>
    protected virtual async Task<string?> GetInitialAnswerAsync(string rephrasedQuestion, CancellationToken cancellationToken)
    {
        var answerer = _context.Answerer;
        var prompt = DebatePrompts.BuildAnswer(rephrasedQuestion);
        int clarifications = 0;

        while (true)
        {
            _observer.OnStatus("Asking the Answerer for an initial answer...");
            var (reply, _) = await SendJsonAsync<AnswererReply>(
                answerer, prompt, AnswerShape, TokenBucket.Answerer, cancellationToken).ConfigureAwait(false);

            // The Answerer wants missing information before it will commit to an answer.
            if (reply?.IsClarification == true && clarifications < MaxAnswererClarifications)
            {
                clarifications++;
                var info = await ResolveAnswererClarificationAsync(reply.Clarification!.Trim(), cancellationToken)
                    .ConfigureAwait(false);
                if (info is null)
                {
                    // User skipped or aborted the clarification.
                    return null;
                }

                prompt = DebatePrompts.BuildClarifiedAnswer(info);
                continue;
            }

            var answer = reply?.Answer?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(answer))
            {
                return answer;
            }

            return await RetryEmptyInitialAnswerAsync(rephrasedQuestion, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Re-asks the Answerer once for a committed answer after it returned nothing. An empty
    /// answer poisons the rest of the pipeline: the restater would have nothing to restate
    /// and tends to "restate" its own instructions, which the Critic then attacks. Returns
    /// the recovered answer, or null if it is still empty (the question is then aborted).
    /// </summary>
    private async Task<string?> RetryEmptyInitialAnswerAsync(string rephrasedQuestion, CancellationToken cancellationToken)
    {
        _observer.OnWarning("Answerer returned no answer; re-asking once.");
        var (retry, _) = await SendJsonAsync<AnswererReply>(
            _context.Answerer,
            DebatePrompts.WithNoThink(DebatePrompts.BuildAnswer(rephrasedQuestion), ModelFor(DebateRole.Answerer)),
            AnswerShape,
            TokenBucket.Answerer,
            cancellationToken)
            .ConfigureAwait(false);
        var retried = retry?.Answer?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(retried))
        {
            return retried;
        }

        _observer.OnWarning("Answerer produced no usable answer — aborting this question.");
        return null;
    }

    /// <summary>
    /// Routes an Answerer clarification request to the user through the Judge rephraser:
    /// surfaces the question, collects the user's reply, then has the rephraser turn that
    /// reply into a neutral statement of fact for the Answerer. Returns that neutral text,
    /// or null if the user skipped (empty) or aborted (null) the clarification.
    /// </summary>
    private async Task<string?> ResolveAnswererClarificationAsync(string answererQuestion, CancellationToken cancellationToken)
    {
        _context.Stats.Clarifications++;
        _observer.OnStatus("The Answerer needs more information; putting its question to you...");
        _observer.OnClarify(answererQuestion);

        var userReply = await _clarifications
            .RequestClarificationAsync(answererQuestion, cancellationToken)
            .ConfigureAwait(false);
        if (userReply is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(userReply))
        {
            _observer.OnWarning("clarification skipped — aborting this question");
            return null;
        }

        _observer.OnStatus("Asking the Judge to rephrase your clarification for the Answerer...");
        var (reply, _) = await SendJsonAsync<JudgeRephraseReply>(
            _context.JudgeRephraser,
            DebatePrompts.BuildClarifyForAnswerer(answererQuestion, userReply),
            RephraseShape, TokenBucket.Rephrase, cancellationToken).ConfigureAwait(false);

        // Prefer the rephraser's neutral version; fall back to the user's raw reply.
        return reply is not null && reply.IsRephrase && !string.IsNullOrWhiteSpace(reply.Text)
            ? reply.Text.Trim()
            : userReply.Trim();
    }

    private static string ComposeVerdict(JudgeVerdictReply verdict)
    {
        var sb = new StringBuilder();
        sb.Append((verdict.Answer ?? string.Empty).Trim());

        if (!string.IsNullOrWhiteSpace(verdict.Justification))
        {
            sb.Append("\n\nWhy: ").Append(verdict.Justification.Trim());
        }

        if (!string.IsNullOrWhiteSpace(verdict.Uncertainty)
            && !NoneNotes.Contains(verdict.Uncertainty.Trim().ToLowerInvariant()))
        {
            sb.Append("\n\nUnresolved: ").Append(verdict.Uncertainty.Trim());
        }

        return sb.ToString();
    }

    private static string FormatDebateTranscript(IReadOnlyList<RoundRecord> rounds)
    {
        if (rounds.Count == 0)
        {
            return EmptyTranscript;
        }

        // Rephrased only: the Answerer's position is shown as the restatement, and its
        // rebuttal to each objection appears as the *next* round's restatement. The raw
        // Answerer text never reaches the arbiter.
        var blocks = rounds.Select((r, idx) =>
            $"Round {idx + 1}\n" +
            $"  Answerer position (neutral restatement): {r.Restatement}\n" +
            $"  Critic objection: {r.Critique}");

        return string.Join("\n\n", blocks);
    }

    // Phase 3

    protected virtual async Task ExtractProfileNoteAsync(IReadOnlyList<string> objections, CancellationToken cancellationToken)
    {
        if (!_context.Config.BuildProfile)
        {
            return;
        }

        // The profiler's only input is the Critic's objections. With none, there is
        // nothing substantive to learn, so skip the call entirely.
        if (objections.Count == 0)
        {
            return;
        }

        JudgeProfileReply? profile;
        try
        {
            _observer.OnStatus("Asking the Judge to extract a profile note...");
            (profile, _) = await SendJsonAsync<JudgeProfileReply>(
                _context.JudgeProfiler, DebatePrompts.BuildProfile(objections), ProfileShape, TokenBucket.Profile, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            _observer.OnWarning($"profile extraction failed: {e.Message}");
            return;
        }

        var note = profile?.Tendency?.Trim().TrimEnd('.');
        if (string.IsNullOrEmpty(note) || NoneNotes.Contains(note.ToLowerInvariant()))
        {
            return;
        }

        if (Profile.IsStylistic(note, out var matched))
        {
            _observer.OnWarning($"profile note rejected — stylistic ('{matched}'): {note}");
            return;
        }

        var update = _context.RecordProfileNote(note);
        _observer.OnProfileUpdate(update);
    }

    /// <summary>
    /// Sends a prompt to an actor and parses a typed JSON reply. On a parse failure,
    /// re-asks exactly once with the expected shape before giving up (returning a null
    /// value). Token usage for every call (including the re-ask) is added to
    /// <paramref name="bucket"/>; the raw final reply is returned for fallbacks.
    /// </summary>
    protected async Task<(T? Value, string Raw)> SendJsonAsync<T>(
        Actor actor, string prompt, string shape, TokenBucket bucket, CancellationToken cancellationToken)
        where T : class
    {
        var raw = await actor.SendAsync(prompt, cancellationToken).ConfigureAwait(false);
        _context.Stats.Add(bucket, CountTokens(prompt, raw));
        if (JsonProtocol.TryParse<T>(raw, out var value, out var error))
        {
            return (value, raw);
        }

        _observer.OnWarning(
            $"{actor.DisplayName} reply was not valid JSON ({error}); re-asking once. " +
            $"Full reply:\n{raw}");

        var reask = DebatePrompts.BuildReask(shape, ModelFor(actor.Role));
        var raw2 = await actor.SendAsync(reask, cancellationToken).ConfigureAwait(false);
        _context.Stats.Add(bucket, CountTokens(reask, raw2));
        if (JsonProtocol.TryParse<T>(raw2, out var value2, out var error2))
        {
            return (value2, raw2);
        }

        _observer.OnWarning(
            $"{actor.DisplayName} reply was still not valid JSON after re-asking ({error2}). " +
            $"Full reply:\n{raw2}");
        return (value2, raw2);
    }

    /// <summary>
    /// Extract a low/medium/high confidence label from free text, or null. Anchors on
    /// the word "confidence" (handling "Confidence: medium" and "medium confidence")
    /// and falls back to the first standalone label. Used as a fallback when the JSON
    /// verdict's confidence field is missing or malformed.
    /// </summary>
    public static ConfidenceLabel? ParseConfidence(string verdict)
    {
        var lowered = verdict.ToLowerInvariant();
        int anchor = lowered.IndexOf("confidence", StringComparison.Ordinal);
        if (anchor != -1)
        {
            var after = ConfidenceLabelRegex().Match(lowered, anchor);
            if (after.Success)
            {
                return ToLabel(after.Groups[1].Value);
            }

            int start = Math.Max(0, anchor - 20);
            var before = lowered.Substring(start, anchor - start);
            var prior = ConfidenceLabelRegex().Matches(before);
            if (prior.Count > 0)
            {
                return ToLabel(prior[^1].Groups[1].Value);
            }
        }

        var match = ConfidenceLabelRegex().Match(lowered);
        return match.Success ? ToLabel(match.Groups[1].Value) : null;
    }

    /// <summary>Maps a confidence string (e.g. "high", "high confidence") to a label, or null.</summary>
    private static ConfidenceLabel? NormalizeConfidence(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = ConfidenceLabelRegex().Match(value);
        return match.Success ? ToLabel(match.Groups[1].Value) : null;
    }

    private static ConfidenceLabel? ToLabel(string value) => value.ToLowerInvariant() switch
    {
        "low" => ConfidenceLabel.Low,
        "medium" => ConfidenceLabel.Medium,
        "high" => ConfidenceLabel.High,
        _ => null,
    };

    /// <summary>The model serving a role, used to tailor model-specific prompt directives.</summary>
    protected string ModelFor(DebateRole role) => _context.Provider.ModelName(role);

    protected int CountTokens(params ReadOnlySpan<string?> texts)
    {
        int total = 0;
        foreach (var t in texts)
        {
            if (!string.IsNullOrEmpty(t))
            {
                total += _tokens.Count(t);
            }
        }

        return total;
    }

    /// <summary>One debate round as the verdict transcript sees it: the Answerer's
    /// position in restated form, paired with the objection raised against it.</summary>
    protected sealed class RoundRecord
    {
        public RoundRecord(string restatement, string critique)
        {
            Restatement = restatement;
            Critique = critique;
        }

        public string Restatement { get; }
        public string Critique { get; }
    }

    /// <summary>What a completed debate hands back: the verdict text (stored for the
    /// rephraser) and the Critic's objections (the profiler's only input).</summary>
    protected readonly record struct DebateOutcome(string VerdictText, IReadOnlyList<string> Objections);
}
