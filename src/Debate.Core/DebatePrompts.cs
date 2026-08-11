namespace Debate.Core;

/// <summary>
/// The per-phase user prompts the pipeline sends to the actors. Each one states the
/// exact JSON shape expected back, so the contract lives in one discoverable place
/// (the <c>!context</c> command renders these) rather than being implied by magic
/// strings scattered across the code and personas.
///
/// Templates use <c>{placeholder}</c> tokens filled by the <c>Build*</c> helpers.
/// </summary>
public static class DebatePrompts
{
    public const string RephraseTemplate =
        "A user asked the question below. Rephrase it into a single neutral, precise " +
        "version. If (and only if) essential information is missing such that the answer " +
        "would genuinely differ, ask ONE clarifying question instead.\n" +
        "Reply with JSON only: {\"action\":\"rephrase\"|\"clarify\",\"text\":\"...\"}\n\n" +
        "USER QUESTION:\n{question}";

    public const string ClarifyFollowUpTemplate =
        "The user replied to your clarifying question (below). Now either ask ONE more " +
        "clarifying question or produce the neutral rephrasing.\n" +
        "Reply with JSON only: {\"action\":\"rephrase\"|\"clarify\",\"text\":\"...\"}\n\n" +
        "USER REPLY:\n{reply}";

    public const string ClarifyForAnswererTemplate =
        "The Answerer needs more information and asked the user the question below; the " +
        "user replied. Rephrase the user's reply into a single neutral, precise statement " +
        "of fact the Answerer can use. Do NOT answer the original debate question yourself.\n" +
        "Reply with JSON only: {\"action\":\"rephrase\",\"text\":\"...\"}\n\n" +
        "ANSWERER'S QUESTION:\n{question}\n\n" +
        "USER REPLY:\n{reply}";

    public const string AnswerTemplate =
        "Answer the question below directly and concisely. Commit to ONE concrete " +
        "recommendation and justify it against the specifics of the question — do not " +
        "list several options as equally valid.\n" +
        "If essential information is genuinely missing such that your recommendation " +
        "would differ, ask ONE focused clarifying question INSTEAD of guessing.\n" +
        "Reply with JSON only, using exactly ONE of these shapes:\n" +
        "- to answer: {\"answer\":\"<your recommendation and why, in prose>\"}\n" +
        "- to ask for missing information: {\"clarification\":\"<your single question>\"}\n" +
        "\"answer\" must be ONE plain-text string of full sentences — NOT a bare list of " +
        "names, and NOT a nested object or sub-fields.\n\n" +
        "QUESTION:\n{question}";

    public const string ClarifiedAnswerTemplate =
        "Here is the information you asked for. Now answer the ORIGINAL question using it. " +
        "Commit to ONE concrete recommendation and justify it.\n" +
        "Reply with JSON only: {\"answer\":\"<your recommendation and why, in prose>\"} " +
        "(or, only if something essential is STILL missing, ask one more question with " +
        "{\"clarification\":\"...\"}).\n\n" +
        "INFORMATION:\n{info}";

    public const string RespondToObjectionTemplate =
        "A reviewer raised the objection below against your answer. Respond to that " +
        "specific objection: concede and revise if it is right, or defend with explicit " +
        "reasoning if it is wrong. Keep your recommendation unless the objection gives a " +
        "concrete reason to change it — do not flip your choice just to appease the " +
        "reviewer.\n" +
        "Reply with JSON only. \"answer\" must be ONE plain-text string of full " +
        "sentences — NOT a bare list of names, and NOT a nested object or sub-fields: " +
        "{\"answer\":\"<your revised-or-defended answer, in prose>\"}\n\n" +
        "OBJECTION:\n{objection}";

    public const string RestateTemplate =
        "Restate the Answerer reply below as neutral, fact-only claims. Strip rhetoric, " +
        "hedging, and persuasive framing; preserve every factual claim, figure, named " +
        "concept, and conditional. Do not add opinion, do not correct, do not contradict, " +
        "do not introduce new information.\n" +
        "Keep these two jobs separate. \"restatement\" is the neutral re-expression and " +
        "nothing else. If the reply asserts something without offering support for it, " +
        "quote that claim in \"unsupported\" — that is where noting a gap belongs, so the " +
        "restatement itself stays free of your judgement. Use an empty list when every " +
        "claim is supported.\n" +
        "Reply with JSON only: {\"restatement\":\"...\",\"unsupported\":[\"<claim>\", ...]}\n\n" +
        "ANSWERER REPLY:\n{answer}";

    public const string CritiqueTemplate =
        "Below is the Answerer's position as a neutral restatement. Weigh its substance — " +
        "unsupported claims, hidden assumptions, edge cases, the strongest opposing view — " +
        "then report your single strongest objection.\n" +
        "Do that weighing in the \"scratch\" field: a few short lines, discarded after you " +
        "reply and never shown to anyone. It exists so the analysis has somewhere to go " +
        "other than the objection itself.\n" +
        "Reply with a JSON object containing EXACTLY these keys and no others: " +
        "{\"scratch\":\"<your brief weighing, discarded>\"," +
        "\"done\":false," +
        "\"objection\":\"<your single strongest objection as ONE plain-text string>\"} " +
        "— or {\"scratch\":\"...\",\"done\":true} if you have no substantive objection left.\n" +
        "Do NOT add any other keys (no \"unsupported_claims\", \"hidden_assumptions\", " +
        "\"edge_cases\", \"strongest_opposing_view\", or the like) — that analysis goes in " +
        "\"scratch\", as prose. \"objection\" is ONE string of one or two sentences — " +
        "never a list, nested object, or bullet points.\n\n" +
        "POSITION:\n{restatement}";

    public const string VerdictTemplate =
        "The debate is over. Below is the exchange in neutral, rephrased form only, round " +
        "by round: the Answerer's position as a neutral restatement, then the Critic's " +
        "objection to it. The Answerer's rebuttal to each objection appears as the next " +
        "round's restatement; an objection with no following round was left unanswered. " +
        "Weigh each objection accordingly.\n" +
        "Reply with JSON only: " +
        "{\"answer\":\"<final answer in plain language>\"," +
        "\"confidence\":\"low\"|\"medium\"|\"high\"," +
        "\"justification\":\"<one sentence>\"," +
        "\"uncertainty\":\"<anything still unresolved, or empty>\"}\n\n" +
        "DEBATE TRANSCRIPT:\n{debate_transcript}";

    public const string ProfileTemplate =
        "Below are the objections the Critic raised against the Answerer during the " +
        "debate — your ONLY source. Report at most ONE substantive tendency they reveal " +
        "about how the Answerer reasons. Ignore all stylistic matters (formatting, tone, " +
        "verbosity). Phrase it tentatively (\"might tend to ...\"). If the objections " +
        "reveal no substantive tendency, report none.\n" +
        "Reply with JSON only: {\"tendency\":\"he might tend to ...\"} or {\"tendency\":null}\n\n" +
        "CRITIC OBJECTIONS:\n{objections}";

    /// <summary>
    /// Qwen3 soft switch that disables chain-of-thought. Applied to retry/nudge prompts and
    /// to the persona files' <c>{no_think}</c> placeholder — a stray "thinking" loop is the
    /// usual reason a reply needed re-asking in the first place.
    ///
    /// Only emitted for models that implement it (see <see cref="SupportsNoThink"/>).
    /// Elsewhere it is not merely inert: an unexplained command-looking token invites a
    /// capable model to acknowledge or query it, which is exactly the non-JSON preamble
    /// the directive exists to prevent.
    /// </summary>
    public const string NoThinkDirective = "/no_think";

    /// <summary>
    /// Whether <paramref name="modelName"/> understands <see cref="NoThinkDirective"/>.
    /// Matched on the name because that is all <see cref="IModelProvider"/> exposes, and it
    /// is what both backends key their own model selection on.
    /// </summary>
    public static bool SupportsNoThink(string? modelName) =>
        modelName is not null && modelName.Contains("qwen", StringComparison.OrdinalIgnoreCase);

    /// <summary>Appended when a reply could not be parsed, to coax a clean retry.</summary>
    public const string ReaskTemplate =
        "Your previous reply was not valid JSON. Reply with ONLY a single JSON object of " +
        "this exact shape and nothing else (no prose, no markdown, no code fences):\n{shape}";

    /// <summary>
    /// The placeholder persona files carry in place of a hardcoded directive, so the
    /// switch is applied per model rather than baked into text shared by every backend.
    /// </summary>
    public const string NoThinkPlaceholder = "{no_think}";

    public static string BuildRephrase(string question) => RephraseTemplate.Replace("{question}", question);

    public static string BuildClarifyFollowUp(string reply) => ClarifyFollowUpTemplate.Replace("{reply}", reply);

    public static string BuildClarifyForAnswerer(string answererQuestion, string userReply) =>
        ClarifyForAnswererTemplate
            .Replace("{question}", answererQuestion)
            .Replace("{reply}", userReply);

    public static string BuildAnswer(string question) => AnswerTemplate.Replace("{question}", question);

    public static string BuildClarifiedAnswer(string info) => ClarifiedAnswerTemplate.Replace("{info}", info);

    public static string BuildRespondToObjection(string objection) =>
        RespondToObjectionTemplate.Replace("{objection}", objection);

    public static string BuildRestate(string answer) => RestateTemplate.Replace("{answer}", answer);

    public static string BuildCritique(string restatement) => CritiqueTemplate.Replace("{restatement}", restatement);

    public static string BuildVerdict(string debateTranscript) =>
        VerdictTemplate.Replace("{debate_transcript}", debateTranscript);

    public static string BuildProfile(IReadOnlyList<string> objections) =>
        ProfileTemplate.Replace(
            "{objections}",
            objections.Count == 0
                ? "(none)"
                : string.Join("\n", objections.Select((o, i) => $"{i + 1}. {o}")));

    public static string BuildReask(string shape, string? modelName) =>
        WithNoThink(ReaskTemplate.Replace("{shape}", shape), modelName);

    /// <summary>
    /// Appends the <see cref="NoThinkDirective"/> to a prompt for nudge/repeat turns, if
    /// <paramref name="modelName"/> is a family that implements it. Otherwise returns the
    /// prompt unchanged.
    /// </summary>
    public static string WithNoThink(string prompt, string? modelName) =>
        SupportsNoThink(modelName) ? $"{prompt}\n\n{NoThinkDirective}" : prompt;

    /// <summary>
    /// Substitutes the <see cref="NoThinkPlaceholder"/> in a rendered persona file with the
    /// directive, or removes it (and the blank line it sat on) for models without it.
    /// </summary>
    public static string ApplyNoThink(string personaText, string? modelName) =>
        SupportsNoThink(modelName)
            ? personaText.Replace(NoThinkPlaceholder, NoThinkDirective)
            : personaText.Replace($"\n{NoThinkPlaceholder}", string.Empty)
                .Replace(NoThinkPlaceholder, string.Empty)
                .TrimEnd();
}
