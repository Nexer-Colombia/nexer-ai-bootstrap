using NexerAI.Core.State;

namespace NexerAI.Core.Ports;

/// <summary>Per-user state file written by the bootstrap (design 4.4); it never holds secrets.</summary>
public interface IStateStore
{
    /// <summary>Returns the stored state, or <see cref="InstallState.Empty"/> when there is no state file yet.</summary>
    /// <exception cref="StateFileException">The state file exists but cannot be understood.</exception>
    Task<InstallState> ReadAsync(CancellationToken ct);

    /// <summary>
    /// Reads the state, applies <paramref name="update"/> and writes the result as one exclusive operation,
    /// so concurrent updates (two terminals running <c>nexer-ai project</c>) do not lose each other's
    /// changes. Nothing is written when <paramref name="update"/> throws.
    /// </summary>
    /// <returns>The state that was written.</returns>
    /// <exception cref="StateFileException">The current state file exists but cannot be understood.</exception>
    Task<InstallState> UpdateAsync(Func<InstallState, InstallState> update, CancellationToken ct);
}
