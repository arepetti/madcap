using Debate.Core;
using Debate.Tests.Fakes;

namespace Debate.Tests.Support;

/// <summary>
/// A fully wired but model-free debate: scripted clients for the three roles, a
/// recording observer, a queued clarification source, and a real
/// <see cref="DebateEngine"/> over temporary persona files. Drives the pipeline end to
/// end so tests can assert the ping/pong and the context-isolation invariants.
/// </summary>
public sealed class Scenario : IDisposable
{
    private readonly TempPersonas _personas;

    public Scenario(
        Func<string, string> answerer,
        Func<string, string> critic,
        Func<string, string> judge,
        int maxRounds = 3,
        bool buildProfile = true,
        params string?[] clarificationReplies)
    {
        _personas = new TempPersonas();
        Provider = new FakeModelProvider(
            new ScriptedChatClient(answerer),
            new ScriptedChatClient(critic),
            new ScriptedChatClient(judge));
        Clarifications = new QueueClarificationSource(clarificationReplies);
        Engine = new DebateEngine(
            TestFactory.Config(maxRounds, buildProfile),
            Provider, _personas.Library, new WordTokenCounter(), Observer, Clarifications);
    }

    public FakeModelProvider Provider { get; }
    public RecordingObserver Observer { get; } = new();
    public QueueClarificationSource Clarifications { get; }
    public DebateEngine Engine { get; }

    public Task RunAsync(string question) => Engine.RunQuestionAsync(question, CancellationToken.None);

    /// <summary>All non-system messages in an actor's buffer, concatenated, for substring checks.</summary>
    public string ActorBuffer(string personaToken)
    {
        var view = Engine.GetActorContexts().Single(a => a.PersonaToken == personaToken);
        return string.Join("\n", view.Messages.Select(m => m.Text));
    }

    public void Dispose() => _personas.Dispose();
}
