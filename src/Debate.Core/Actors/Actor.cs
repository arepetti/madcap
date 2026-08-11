using Microsoft.Extensions.AI;

namespace Debate.Core.Actors;

/// <summary>
/// Base class for an LLM-backed participant. Holds its own conversation history
/// as a list of <see cref="ChatMessage"/> (the idiomatic Microsoft.Extensions.AI
/// equivalent of the original AutoGen agent's model context) and talks to the
/// model only through the provider's <see cref="IChatClient"/>.
///
/// Temperature is applied per call via <see cref="ChatOptions"/> rather than
/// baked into a client, which is the idiomatic .NET pattern.
/// </summary>
public abstract class Actor
{
    protected Actor(DebateContext context)
    {
        Context = context;
    }

    protected DebateContext Context { get; }

    public abstract DebateRole Role { get; }
    public abstract string DisplayName { get; }

    /// <summary>
    /// The persona file token this actor loads ("&lt;preset&gt;.&lt;token&gt;.txt").
    /// Defaults to the role token; the Judge sub-roles override it so each single-job
    /// context has its own system prompt.
    /// </summary>
    public virtual string PersonaToken => Role.ToToken();

    protected float Temperature => Context.Config.TemperatureFor(Role);

    /// <summary>
    /// Conversation buffer including the system message at index 0. Null until
    /// first use so system prompts (which may embed cross-round state) are
    /// rendered as late as possible.
    /// </summary>
    private List<ChatMessage>? _history;

    /// <summary>The model serving this actor, used to tailor model-specific directives.</summary>
    protected string ModelName => Context.Provider.ModelName(Role);

    /// <summary>
    /// Build the system prompt. Default loads the persona file verbatim;
    /// roles whose prompt embeds session state override this.
    /// </summary>
    protected virtual string RenderSystemPrompt() =>
        Context.Personas.Load(Context.Config.PersonaName, PersonaToken);

    /// <summary>
    /// The system prompt as the model actually receives it: the rendered persona with
    /// model-specific directives resolved. Kept out of <see cref="RenderSystemPrompt"/> so
    /// overrides only have to deal with their own placeholders.
    /// </summary>
    private string BuildSystemPrompt() => DebatePrompts.ApplyNoThink(RenderSystemPrompt(), ModelName);

    private List<ChatMessage> History()
    {
        return _history ??= [new(ChatRole.System, BuildSystemPrompt())];
    }

    /// <summary>True once the conversation buffer (and system prompt) has been built.</summary>
    public bool IsBuilt => _history is not null;

    /// <summary>Current buffer contents, for token accounting. Empty if not built.</summary>
    public IReadOnlyList<ChatMessage> Messages => _history ?? [];

    /// <summary>
    /// Render this actor's system prompt without mutating its buffer. Used by the
    /// <c>!context</c> command to show exactly what the actor would be primed with.
    /// </summary>
    public string PreviewSystemPrompt() => BuildSystemPrompt();

    /// <summary>
    /// Force a rebuild (fresh system prompt + empty conversation) on next use.
    /// Used for per-round actors whose system prompt reflects the latest history
    /// and profile.
    /// </summary>
    public void Invalidate() => _history = null;

    /// <summary>Wipe the conversation buffer (used on a new session).</summary>
    public Task ResetMemoryAsync()
    {
        _history = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Send one user-sourced message and return the stripped reply. Mutates the
    /// conversation buffer so multi-turn memory is preserved within the actor's
    /// lifetime.
    /// </summary>
    public async Task<string> SendAsync(string userText, CancellationToken cancellationToken)
    {
        var history = History();
        var userMessage = new ChatMessage(ChatRole.User, userText);

        var options = new ChatOptions { Temperature = Temperature };
        if (Context.Provider.MaxOutputTokens(Role) is int maxTokens and > 0)
        {
            options.MaxOutputTokens = maxTokens;
        }

        // Send the new turn without committing it: a failed or cancelled call must leave
        // the buffer exactly as it was. The Answerer is never invalidated, so a dangling
        // user turn here would survive into later questions and send the model two
        // consecutive user messages with no assistant turn between them.
        var response = await Context.Provider
            .GetClient(Role)
            .GetResponseAsync([.. history, userMessage], options, cancellationToken)
            .ConfigureAwait(false);

        var text = (response.Text ?? string.Empty).Trim();

        history.Add(userMessage);

        // Persist the reply WITHOUT any <think> reasoning. A reply that is all reasoning
        // (a degenerate thinking loop) would otherwise stay in the buffer and be re-read
        // on the next turn — including the automatic re-ask after a parse failure — which
        // just feeds the loop. The caller still gets the raw text to parse.
        history.Add(new ChatMessage(ChatRole.Assistant, JsonProtocol.StripReasoning(text)));
        return text;
    }
}
