using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Core.Tests.Fakes;

public sealed class FakeIssueReporter : IIssueReporter
{
    public Uri IssueUrl { get; init; } = new("https://github.com/example/repo/issues/1");

    public List<IssueDraft> Drafts { get; } = [];

    public Task<Uri> CreateIssueAsync(IssueDraft draft, CancellationToken ct)
    {
        Drafts.Add(draft);
        return Task.FromResult(IssueUrl);
    }
}
