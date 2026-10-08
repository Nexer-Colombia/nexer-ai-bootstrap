# nexer-ai-bootstrap

Installer for the Nexer Colombia AI adoption program. `nexer-ai` is a Windows PowerShell 5.1 module, run without admin rights, that installs Claude Code plus the plugins and MCP servers approved for a person's role and project, and keeps them up to date. This repository holds only the installer and contains no secrets.

## Layout

| Path | Contents |
|---|---|
| `NexerAI/NexerAI.psd1`, `NexerAI.psm1` | Module manifest and loader (dot-sources `Private/` then `Public/`) |
| `NexerAI/Private/Ports.ps1` | Port contracts, the single source of truth for required method names |
| `NexerAI/Public/` | Exported functions, one per file |
| `tests/` | Pester tests (`*.Tests.ps1`) |
| `tests/Fakes/Fakes.ps1` | In-memory port fakes for tests; not shipped in the module |

## Ports

The core is hexagonal. A port is a plain `[pscustomobject]` with `ScriptMethod` members (not a PowerShell class, so adapters and fakes need no `using module` and no type caching). `Assert-NexerPort -Port $obj -Name <Port>` checks an object against its contract; `Get-NexerPortContract` returns the contracts.

| Port | Direction | Methods |
|---|---|---|
| `ProfileSource` | Driving | `GetProfiles($channel)`, `GetPluginNames($channel)` |
| `StackDetector` | Driving | `Detect($path)` |
| `Prompter` | Driving | `Choose($question, $choices)`, `ReadSecret($prompt)`, `Confirm($question)` |
| `AgentInstaller` | Driven | `GetInstalledPlugins()`, `InstallPlugin($name, $scope, $config)`, `AddMcpServer($name, $scope, $command)` |
| `CredentialStore` | Driven | `Test($url)`, `Get($url)`, `Set($url, $secret)` (secret as `SecureString`) |
| `IssueReporter` | Driven | `CreateIssue($title, $body, $labels)` returns the issue URL |

In tests, `New-NexerFake -Port <Port> -Returns @{ Method = <value or scriptblock> }` builds a fake that satisfies the contract, records every call in `.Calls` (`Method`, `Arguments`) and returns the canned value, or the scriptblock's result for the call arguments.

## Running the tests

Windows PowerShell 5.1 with Pester 6.2.0 for the current user:

```powershell
Install-Module Pester -RequiredVersion 6.2.0 -Scope CurrentUser -Force -SkipPublisherCheck
Import-Module Pester -RequiredVersion 6.2.0
Invoke-Pester -Path tests -Output Detailed
```

CI runs the same suite on `windows-latest` with `powershell` (not `pwsh`).
