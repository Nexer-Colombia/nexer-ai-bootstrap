# Single source of truth for the port contracts: port name -> required method names.
$script:NexerPortContracts = [ordered]@{
    ProfileSource   = @('GetProfiles', 'GetPluginNames')
    StackDetector   = @('Detect')
    Prompter        = @('Choose', 'ReadSecret', 'Confirm')
    AgentInstaller  = @('GetInstalledPlugins', 'InstallPlugin', 'AddMcpServer')
    CredentialStore = @('Test', 'Get', 'Set')
    IssueReporter   = @('CreateIssue')
}
