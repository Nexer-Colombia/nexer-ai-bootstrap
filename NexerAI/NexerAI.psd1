@{
    RootModule        = 'NexerAI.psm1'
    ModuleVersion     = '0.1.0'
    GUID              = 'b7c3e2f4-5a61-4d8e-9f0a-3c2d1e6b8a47'
    Author            = 'Nexer Colombia'
    CompanyName       = 'Nexer Colombia'
    Description       = 'Installs Claude Code plus the plugins and MCP servers approved for a role and project.'
    PowerShellVersion = '5.1'
    FunctionsToExport = @('Assert-NexerPort', 'Get-NexerPortContract')
    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()
}
