using Debate.Core;

namespace Debate.Tests.Fakes;

/// <summary>Captures every observer callback so tests can assert what the user saw.</summary>
public sealed class RecordingObserver : IDebateObserver
{
    public List<string> Rephrased { get; } = new();
    public List<string> Clarify { get; } = new();
    public List<string> Answerer { get; } = new();
    public List<string> Restatement { get; } = new();
    public List<string> Critic { get; } = new();
    public List<(string Text, ConfidenceLabel? Confidence)> Verdict { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Info { get; } = new();
    public List<string> Status { get; } = new();
    public List<ProfileUpdate> ProfileUpdates { get; } = new();

    public void OnRephrased(string question) => Rephrased.Add(question);
    public void OnClarify(string question) => Clarify.Add(question);
    public void OnAnswerer(string text) => Answerer.Add(text);
    public void OnRestatement(string text) => Restatement.Add(text);
    public void OnCritic(string text) => Critic.Add(text);
    public void OnVerdict(string text, ConfidenceLabel? confidence) => Verdict.Add((text, confidence));
    public void OnWarning(string text) => Warnings.Add(text);
    public void OnInfo(string text) => Info.Add(text);
    public void OnStatus(string text) => Status.Add(text);
    public void OnProfileUpdate(ProfileUpdate update) => ProfileUpdates.Add(update);
}
