using System.Text.Json;
using System.Text.Json.Serialization;

namespace Debate.Models.FoundryLocal;

/// <summary>
/// One chat message in a <see cref="HostRequest"/>: a role
/// (<c>system</c>/<c>user</c>/<c>assistant</c>) and its text.
/// </summary>
public sealed class HostMessage
{
    public string Role { get; set; } = "user";
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// A line the parent writes to a model host's stdin: either a chat request
/// (<see cref="Messages"/> set) or a shutdown signal (<see cref="Shutdown"/> true).
/// </summary>
public sealed class HostRequest
{
    /// <summary>Correlates the reply with this request.</summary>
    public int Id { get; set; }

    /// <summary>Sampling temperature, if any.</summary>
    public float? Temperature { get; set; }

    /// <summary>Cap on tokens generated for this reply, if any (null = unbounded).</summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>Full conversation to complete (system + prior turns + new user turn).</summary>
    public List<HostMessage> Messages { get; set; } = [];

    /// <summary>When true, the host should finish and exit; other fields are ignored.</summary>
    public bool Shutdown { get; set; }
}

/// <summary>
/// A line a model host writes to stdout: a one-time readiness signal
/// (<see cref="Ready"/>), or a reply carrying either <see cref="Text"/> or
/// <see cref="Error"/> correlated by <see cref="Id"/>.
/// </summary>
public sealed class HostResponse
{
    /// <summary>True on the single readiness line emitted once the model is loaded.</summary>
    public bool Ready { get; set; }

    /// <summary>The loaded model id, included on the readiness line.</summary>
    public string? Model { get; set; }

    /// <summary>Echoes the originating <see cref="HostRequest.Id"/> for replies.</summary>
    public int? Id { get; set; }

    /// <summary>The completion text on success.</summary>
    public string? Text { get; set; }

    /// <summary>A human-readable error message when the completion failed.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// Line-delimited JSON framing for the model host protocol. Each message is a single
/// compact JSON object on its own line; newlines only ever separate messages.
/// </summary>
public static class ModelHostProtocol
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // A compact, single-line payload is required by the line-delimited framing.
        WriteIndented = false,
    };

    public static string SerializeRequest(HostRequest request) =>
        JsonSerializer.Serialize(request, Options);

    public static HostRequest? DeserializeRequest(string line) =>
        JsonSerializer.Deserialize<HostRequest>(line, Options);

    public static string SerializeResponse(HostResponse response) =>
        JsonSerializer.Serialize(response, Options);

    public static HostResponse? DeserializeResponse(string line) =>
        JsonSerializer.Deserialize<HostResponse>(line, Options);
}
