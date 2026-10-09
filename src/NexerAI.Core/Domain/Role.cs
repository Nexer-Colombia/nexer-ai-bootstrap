namespace NexerAI.Core.Domain;

/// <summary>Answer to the role question of <c>nexer-ai install</c>; it selects the role plugin.</summary>
public enum Role
{
    /// <summary>Developer: <c>nexer-core</c> only.</summary>
    Developer,

    /// <summary>QA: adds <c>nexer-qa</c>.</summary>
    Qa,

    /// <summary>PM/PO: adds <c>nexer-po</c>.</summary>
    ProductOwner,
}

/// <summary>The command-line tokens of <see cref="Role"/>: <c>developer</c>, <c>qa</c> and <c>po</c>.</summary>
public static class RoleTokens
{
    /// <summary>All tokens, in the order the role question offers them.</summary>
    public static IReadOnlyList<string> All { get; } = ["developer", "qa", "po"];

    /// <summary>Finds a role by its exact (case-sensitive) token; numeric strings are not accepted.</summary>
    public static bool TryParse(string? token, out Role role)
    {
        (var found, role) = token switch
        {
            "developer" => (true, Role.Developer),
            "qa" => (true, Role.Qa),
            "po" => (true, Role.ProductOwner),
            _ => (false, default),
        };
        return found;
    }

    /// <summary>Finds a role by its token or throws <see cref="ArgumentException"/>.</summary>
    public static Role Parse(string token) =>
        TryParse(token, out var role)
            ? role
            : throw new ArgumentException($"Unknown role '{token}'. Expected one of: {string.Join(", ", All)}.", nameof(token));

    /// <summary>Returns the token of a defined role.</summary>
    public static string ToToken(this Role role) => role switch
    {
        Role.Developer => "developer",
        Role.Qa => "qa",
        Role.ProductOwner => "po",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Undefined role."),
    };
}
