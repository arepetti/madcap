using Debate.Core.Actors;

namespace Debate.Core;

/// <summary>
/// Shared, session-wide state: config, the model provider, persona library,
/// cross-round history and profile, per-session stats, and the three actors.
/// Ports <c>context.py</c> including the profile merge policy.
/// </summary>
public sealed class DebateContext
{
    public DebateContext(SessionConfig config, IModelProvider provider, PersonaLibrary personas)
    {
        Config = config;
        Provider = provider;
        Personas = personas;

        Answerer = new Answerer(this);
        Critic = new Critic(this);
        JudgeRephraser = new JudgeRephraser(this);
        JudgeRestater = new JudgeRestater(this);
        JudgeArbiter = new JudgeArbiter(this);
        JudgeProfiler = new JudgeProfiler(this);
    }

    public SessionConfig Config { get; }
    public IModelProvider Provider { get; }
    public PersonaLibrary Personas { get; }

    /// <summary>
    /// How many past question/verdict pairs are kept. Both lists are rendered into system
    /// prompts on every question, so they compete with the debate itself for the context
    /// window: unbounded, a long session eventually crowds out the current question.
    /// </summary>
    public const int MaxPriorExchanges = 5;

    /// <summary>
    /// How much of each verdict is kept for that history. A full verdict (answer,
    /// justification and unresolved notes) runs to hundreds of tokens; the rephraser only
    /// needs enough to recognise the topic of a follow-up question.
    /// </summary>
    public const int MaxStoredVerdictLength = 400;

    public List<string> PriorRephrased { get; } = new();

    /// <summary>
    /// The verdict reached for each prior rephrased question (aligned by index with
    /// <see cref="PriorRephrased"/>). Shown only to the rephraser so it can resolve
    /// follow-up questions; never fed back into the debate itself.
    /// </summary>
    public List<string> PriorVerdicts { get; } = new();
    public List<ProfileEntry> ProfileEntries { get; } = new();
    public SessionStats Stats { get; } = new();

    public Answerer Answerer { get; }
    public Critic Critic { get; }
    public JudgeRephraser JudgeRephraser { get; }
    public JudgeRestater JudgeRestater { get; }
    public JudgeArbiter JudgeArbiter { get; }
    public JudgeProfiler JudgeProfiler { get; }

    /// <summary>
    /// Records a completed question and its verdict for the rephraser's session memory,
    /// keeping only the most recent <see cref="MaxPriorExchanges"/> and storing each
    /// verdict in abbreviated form. The two lists stay index-aligned.
    /// </summary>
    public void RecordExchange(string rephrased, string verdictText)
    {
        PriorRephrased.Add(rephrased);
        PriorVerdicts.Add(Abbreviate(verdictText, MaxStoredVerdictLength));

        while (PriorRephrased.Count > MaxPriorExchanges)
        {
            PriorRephrased.RemoveAt(0);
            PriorVerdicts.RemoveAt(0);
        }
    }

    private static string Abbreviate(string text, int maxLength)
    {
        text = text.Trim();
        return text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength), "...");
    }

    /// <summary>The four single-job Judge contexts, in pipeline order.</summary>
    public IEnumerable<Actor> JudgeContexts()
    {
        yield return JudgeRephraser;
        yield return JudgeRestater;
        yield return JudgeArbiter;
        yield return JudgeProfiler;
    }

    /// <summary>Notes observed often enough to be shown to the Critic.</summary>
    public IReadOnlyList<string> ActiveProfile() =>
        ProfileEntries.Where(e => e.Count >= Profile.MinCountToSurface).Select(e => e.Text).ToList();

    /// <summary>Candidate notes still hidden from the Critic.</summary>
    public IReadOnlyList<ProfileEntry> PendingProfile() =>
        ProfileEntries.Where(e => e.Count < Profile.MinCountToSurface).ToList();

    /// <summary>
    /// Merge a note into the profile, or insert it as a new candidate. Picks the
    /// most similar existing entry; if similarity clears the threshold its count
    /// is incremented, otherwise a fresh entry is appended (evicting the oldest
    /// single-occurrence candidate first when at capacity). Returns a description
    /// of what happened.
    /// </summary>
    public ProfileUpdate RecordProfileNote(string note)
    {
        int bestIndex = -1;
        double bestSimilarity = 0.0;
        for (int i = 0; i < ProfileEntries.Count; i++)
        {
            double sim = Profile.Similarity(note, ProfileEntries[i].Text);
            if (sim > bestSimilarity)
            {
                bestSimilarity = sim;
                bestIndex = i;
            }
        }

        if (bestIndex >= 0 && bestSimilarity >= Profile.SimilarityThreshold)
        {
            var entry = ProfileEntries[bestIndex];
            entry.Count++;
            var kind = entry.Count == Profile.MinCountToSurface
                ? ProfileUpdateKind.BecameActive
                : ProfileUpdateKind.Incremented;
            return new ProfileUpdate(kind, entry.Text, entry.Count);
        }

        if (ProfileEntries.Count >= Profile.MaxEntries)
        {
            int victim = ProfileEntries.FindIndex(e => e.Count == 1);
            ProfileEntries.RemoveAt(victim >= 0 ? victim : 0);
        }

        var fresh = new ProfileEntry(note);
        ProfileEntries.Add(fresh);
        return new ProfileUpdate(ProfileUpdateKind.NewCandidate, fresh.Text, fresh.Count);
    }

    public IEnumerable<Actor> AllActors()
    {
        yield return Answerer;
        yield return Critic;
        yield return JudgeRephraser;
        yield return JudgeRestater;
        yield return JudgeArbiter;
        yield return JudgeProfiler;
    }

    public async Task ClearSessionAsync()
    {
        PriorRephrased.Clear();
        PriorVerdicts.Clear();
        ProfileEntries.Clear();
        Stats.Reset();
        foreach (var actor in AllActors())
        {
            await actor.ResetMemoryAsync().ConfigureAwait(false);
        }

        // Critic and the Judge contexts are rebuilt per round; drop them.
        Critic.Invalidate();
        foreach (var judge in JudgeContexts())
        {
            judge.Invalidate();
        }
    }
}
