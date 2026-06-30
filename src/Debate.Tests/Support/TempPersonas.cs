using Debate.Core;

namespace Debate.Tests.Support;

/// <summary>
/// Creates a throwaway persona directory with one minimal file per required token, so
/// pipeline tests exercise the real <see cref="PersonaLibrary"/> loading and placeholder
/// substitution without depending on the shipped persona content. The bodies are short
/// markers that also let a test confirm the right persona reached the right actor.
/// Dispose deletes the directory.
/// </summary>
public sealed class TempPersonas : IDisposable
{
    public TempPersonas(string preset = "default")
    {
        Directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "debate-tests-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);

        Write(preset, PersonaTokens.Answerer, "PERSONA:answerer");
        Write(preset, PersonaTokens.Critic,
            "PERSONA:critic\nPRIOR:{prior_rephrased}\nPROFILE:{answerer_profile}");
        Write(preset, PersonaTokens.JudgeRephraser, "PERSONA:judge-rephraser\nEXCHANGES:{prior_exchanges}");
        Write(preset, PersonaTokens.JudgeRestater, "PERSONA:judge-restater");
        Write(preset, PersonaTokens.JudgeArbiter, "PERSONA:judge-arbiter");
        Write(preset, PersonaTokens.JudgeProfiler, "PERSONA:judge-profiler");

        Library = new PersonaLibrary(Directory);
    }

    public string Directory { get; }

    public PersonaLibrary Library { get; }

    private void Write(string preset, string token, string body) =>
        System.IO.File.WriteAllText(System.IO.Path.Combine(Directory, $"{preset}.{token}.txt"), body);

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; a leftover temp dir must not fail a test.
        }
    }
}
