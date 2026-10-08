# nexer-ai-bootstrap

Installer for the Nexer Colombia AI adoption program. `nexer-ai` installs Claude Code plus the plugins and MCP servers approved for a person's role and project on Windows, without admin rights, and keeps them up to date. It is a C# (.NET 10) console app published as a single Native AOT executable for `win-x64`. This repository holds only the installer and contains no secrets.

Status: skeleton. Only `nexer-ai --version` works; `install`, `project`, `doctor`, `statusline` and `mcp-launch` come next.

## Layout

| Path | Contents |
|---|---|
| `src/NexerAI.Core` | Domain types and ports. No I/O, no process calls, AOT compatible |
| `src/NexerAI.Cli` | `nexer-ai.exe`, the composition root that wires adapters to the core |
| `tests/NexerAI.Core.Tests` | xUnit v3 tests for the core |
| `test-vectors` | Language-agnostic test vectors shared with other implementations |

## Ports

The core is hexagonal: it depends only on these interfaces (`src/NexerAI.Core/Ports`).

| Port | Purpose |
|---|---|
| `IProfileSource` | Project profiles and plugin catalog from the marketplace, at the channel's git ref |
| `IStackDetector` | Technologies found in a working copy, to suggest stack plugins |
| `IPrompter` | Interactive questions and masked secret input |
| `IAgentInstaller` | Installed plugins, plugin install and MCP registration (Claude Code adapter only) |
| `ICredentialStore` | Per-user tracker tokens keyed by tracker URL |
| `IIssueReporter` | GitHub issues, for example a profile request for an unknown repository |

## Test vectors

`test-vectors/remotes.json` defines how a git remote is normalized to the canonical `host/path` key that `nexer-ai project` matches against the `repos` of project profiles. Each case has an `input`, the `expected` key (`null` means the input must be rejected) and a `note` with the reason.

The file is a contract. Marketplace CI applies the same rule to reject two profiles that list the same remote, and the Nexer mod will use it later; every implementation must pass every case. This repository is the only source: consumers download the file pinned to a bootstrap tag, never to a branch:

```text
https://raw.githubusercontent.com/Nexer-Colombia/nexer-ai-bootstrap/<tag>/test-vectors/remotes.json
```

Changing a vector needs a pull request here (with the matching code change) and a new tag; consumers then move their pin. The xUnit theory in `tests/NexerAI.Core.Tests/Remotes` runs every case, so a new vector needs no test code.

## Build and test

Requires the .NET SDK pinned in `global.json`. Tests run on Microsoft.Testing.Platform (configured in `global.json`).

```powershell
dotnet build
dotnet test
```

Native AOT publish (`dotnet publish src/NexerAI.Cli -c Release -r win-x64`) needs the Visual Studio C++ build tools; without them it fails with "Platform linker not found". CI publishes the AOT executable and runs `nexer-ai.exe --version` on every push and pull request, so locally `build` and `test` are enough.
