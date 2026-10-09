using NexerAI.Core.Domain;

namespace NexerAI.Core.Planning;

/// <summary>One change to the agent's state. The cases below are the only ones.</summary>
public abstract record PlanAction
{
    private PlanAction()
    {
    }

    /// <summary>Install the plugin with <see cref="Ports.IAgentInstaller.InstallPluginAsync"/>.</summary>
    public sealed record InstallPlugin(PluginInstallRequest Request) : PlanAction;

    /// <summary>Register the MCP server with <see cref="Ports.IAgentInstaller.AddMcpServerAsync"/>.</summary>
    public sealed record AddMcpServer(McpServerRegistration Registration) : PlanAction;
}
