using System.Globalization;

namespace NexerAI.Core.StatusLine;

/// <summary>Reads <c>prompt_cache.ttl</c>.</summary>
internal static class CacheTtl
{
    /// <summary>
    /// The length of a ttl made of one to three ASCII digits and a unit, such as <c>5m</c> or <c>1h</c>; null
    /// for any other ttl, which is then dropped, so no input text other than this reaches the terminal (it
    /// could carry escape sequences).
    /// </summary>
    public static TimeSpan? Parse(string? ttl)
    {
        if (ttl is not { Length: >= 2 and <= 4 })
        {
            return null;
        }

        var digits = ttl.AsSpan(0, ttl.Length - 1);
        if (digits.IndexOfAnyExceptInRange('0', '9') >= 0)
        {
            return null;
        }

        var amount = int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        return ttl[^1] switch
        {
            's' => TimeSpan.FromSeconds(amount),
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            _ => null,
        };
    }
}
