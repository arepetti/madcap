namespace Debate.Tests.Support;

/// <summary>
/// Recognises which pipeline phase a prompt belongs to by a stable substring of its
/// template. Lets a scripted client route replies by intent rather than call order,
/// which keeps the four Judge contexts (all sharing one client) apart.
/// </summary>
public static class Phase
{
    // Judge (rephraser) contexts
    public static bool IsRephrase(string p) => p.Contains("Rephrase it into a single neutral");
    public static bool IsClarifyFollowUp(string p) => p.Contains("replied to your clarifying question");
    public static bool IsClarifyForAnswerer(string p) => p.Contains("The Answerer needs more information");

    // Other Judge contexts
    public static bool IsRestate(string p) => p.Contains("Restate the Answerer reply");
    public static bool IsVerdict(string p) => p.Contains("The debate is over.");
    public static bool IsProfile(string p) => p.Contains("Report at most ONE substantive tendency");

    // Answerer
    public static bool IsAnswer(string p) => p.Contains("Answer the question below directly");
    public static bool IsClarifiedAnswer(string p) => p.Contains("Here is the information you asked for");
    public static bool IsRespondToObjection(string p) => p.Contains("A reviewer raised the objection below");

    // Critic
    public static bool IsCritique(string p) => p.Contains("Challenge its substance");

    // Shared
    public static bool IsReask(string p) => p.Contains("was not valid JSON");
}
