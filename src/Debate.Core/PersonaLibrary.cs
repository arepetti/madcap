namespace Debate.Core;

/// <summary>
/// Loads persona system-prompt files and renders their placeholders. Ports
/// <c>personas.py</c>. A persona file is named "&lt;preset&gt;.&lt;role&gt;.txt";
/// missing files fall back to "default.&lt;role&gt;.txt".
///
/// The directory is supplied by the host (so the same core works regardless of
/// where the files live), defaulting to a "personas" folder next to the app.
/// </summary>
public sealed class PersonaLibrary
{
    private readonly string _directory;

    public PersonaLibrary(string directory)
    {
        _directory = directory;
    }

    public string Directory => _directory;

    /// <summary>All persona presets present on disk (anything with an .answerer.txt).</summary>
    public IReadOnlyList<string> ListPersonaNames()
    {
        if (!System.IO.Directory.Exists(_directory))
        {
            return [];
        }

        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in System.IO.Directory.EnumerateFiles(_directory, "*.answerer.txt"))
        {
            var fileName = Path.GetFileName(path);
            var preset = fileName.Split('.')[0];
            names.Add(preset);
        }

        return names.ToList();
    }

    /// <summary>
    /// Resolve the file for a (preset, token), trying the preset then the
    /// "default" fallback. Returns null if neither exists.
    /// </summary>
    public string? ResolvePersonaPath(string personaName, string token)
    {
        var candidates = new[]
        {
            Path.Combine(_directory, $"{personaName}.{token}.txt"),
            Path.Combine(_directory, $"default.{token}.txt"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public string Load(string personaName, string token)
    {
        var path = ResolvePersonaPath(personaName, token);
        if (path is null)
        {
            throw new FileNotFoundException(
                $"No persona file for token '{token}' " +
                $"(tried '{personaName}.{token}.txt' and 'default.{token}.txt') in '{_directory}'");
        }

        return File.ReadAllText(path).Trim();
    }

    public static string RenderPriorRephrased(IReadOnlyList<string> prior)
    {
        if (prior.Count == 0)
        {
            return "(none yet)";
        }

        return string.Join("\n", prior.Select((q, i) => $"{i + 1}. {q}"));
    }

    /// <summary>
    /// Render the rephraser's session memory: each prior rephrased question paired
    /// with the verdict the debate reached. Lets the rephraser keep terminology
    /// consistent and resolve follow-up questions that refer back to an earlier topic.
    /// </summary>
    public static string RenderPriorExchanges(IReadOnlyList<string> rephrased, IReadOnlyList<string> verdicts)
    {
        if (rephrased.Count == 0)
        {
            return "(none yet)";
        }

        return string.Join("\n\n", rephrased.Select((q, i) =>
        {
            var verdict = i < verdicts.Count ? verdicts[i] : null;
            return string.IsNullOrWhiteSpace(verdict)
                ? $"{i + 1}. Q: {q}"
                : $"{i + 1}. Q: {q}\n   Verdict: {verdict}";
        }));
    }

    public static string RenderAnswererProfile(IReadOnlyList<string> profile)
    {
        if (profile.Count == 0)
        {
            return "(none yet)";
        }

        return string.Join("\n", profile.Select(item => $"- {item}"));
    }
}
