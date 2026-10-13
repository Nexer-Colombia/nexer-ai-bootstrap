using System.Security.Cryptography;
using System.Text;

namespace NexerAI.Core.Tasks;

/// <summary>
/// Identifies the task of a working copy (design 4.6): the lowercase repository root path with forward
/// slashes and the branch, as <c>&lt;root&gt;#&lt;branch&gt;</c>. Both are local values that never leave the machine.
/// </summary>
public sealed record TaskKey(string RepositoryRoot, string Branch)
{
    /// <summary>The key, <c>&lt;root&gt;#&lt;branch&gt;</c>.</summary>
    public string Value => $"{RepositoryRoot}#{Branch}";

    /// <summary>Lowercase hex SHA-256 of the UTF-8 key: the task file is named <c>&lt;Sha256&gt;.json</c>.</summary>
    public string Sha256 => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Value)));

    /// <inheritdoc />
    public override string ToString() => Value;
}
