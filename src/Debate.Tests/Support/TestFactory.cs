using Debate.Core;
using Debate.Tests.Fakes;

namespace Debate.Tests.Support;

/// <summary>Small constructors for core objects used across tests.</summary>
public static class TestFactory
{
    public static SessionConfig Config(int maxRounds = 3, bool buildProfile = true, string persona = "default") =>
        new(persona, 0.3f, 0.9f, 0.3f, buildProfile, maxRounds);

    public static FakeModelProvider Provider(
        Func<string, string> answerer, Func<string, string> critic, Func<string, string> judge) =>
        new(new ScriptedChatClient(answerer), new ScriptedChatClient(critic), new ScriptedChatClient(judge));

    /// <summary>A provider whose clients never need to respond (for non-pipeline tests).</summary>
    public static FakeModelProvider InertProvider()
    {
        static string Inert(string _) => "{}";
        return Provider(Inert, Inert, Inert);
    }
}
