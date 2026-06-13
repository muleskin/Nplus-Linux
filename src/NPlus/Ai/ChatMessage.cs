namespace NPlus.Ai;

/// <summary>A single chat turn. <paramref name="Role"/> is "system", "user" or "assistant".</summary>
public sealed record ChatMessage(string Role, string Content)
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
}
