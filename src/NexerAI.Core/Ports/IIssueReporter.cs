using NexerAI.Core.Domain;

namespace NexerAI.Core.Ports;

/// <summary>Opens issues in the marketplace repository.</summary>
public interface IIssueReporter
{
    /// <summary>Creates the issue and returns its URL.</summary>
    Task<Uri> CreateIssueAsync(IssueDraft draft, CancellationToken ct);
}
