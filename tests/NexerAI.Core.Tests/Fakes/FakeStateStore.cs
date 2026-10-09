using NexerAI.Core.Ports;
using NexerAI.Core.State;

namespace NexerAI.Core.Tests.Fakes;

public sealed class FakeStateStore : IStateStore
{
    public InstallState State { get; set; } = InstallState.Empty;

    public int UpdateCount { get; private set; }

    public Task<InstallState> ReadAsync(CancellationToken ct) => Task.FromResult(State);

    public Task<InstallState> UpdateAsync(Func<InstallState, InstallState> update, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(update);
        State = update(State) ?? throw new InvalidOperationException("The state update returned null.");
        UpdateCount++;
        return Task.FromResult(State);
    }
}
