namespace ModularAudience.Forms.Modules.Dialogs;

public sealed record BreakbeatChatMessage(string role, string content);

/// <summary>RAM-only complete turns. No chat contents are written to settings.</summary>
public sealed class BreakbeatLlmContext
{
    public const int MaximumTurns = 8;
    public const int MaximumCharacters = 64000;
    private readonly List<(string User, string Assistant)> turns = [];
    private string? identity;
    public int Generation { get; private set; }
    public int TurnCount => turns.Count;
    public void Clear() { turns.Clear(); identity = null; Generation++; }
    public BreakbeatChatMessage[] Messages(string endpointAndModel, string system, string user, bool keep)
    {
        if (!keep) return [new("system", system), new("user", user)];
        if (identity != endpointAndModel) { Clear(); identity = endpointAndModel; }
        var result = new List<BreakbeatChatMessage> { new("system", system) };
        foreach (var turn in turns) { result.Add(new("user", turn.User)); result.Add(new("assistant", turn.Assistant)); }
        result.Add(new("user", user)); return result.ToArray();
    }
    public void Remember(int generation, string user, string assistant)
    {
        if (generation != Generation || identity == null) return;
        if (user.Length + assistant.Length > MaximumCharacters) { turns.Clear(); return; }
        turns.Add((user, assistant));
        while (turns.Count > MaximumTurns || turns.Sum(t => t.User.Length + t.Assistant.Length) > MaximumCharacters) turns.RemoveAt(0);
    }
}
