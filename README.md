# nexer-ai-bootstrap

Installer for the Nexer Colombia AI adoption program. `nexer-ai` installs Claude Code plus the plugins and MCP servers approved for a person's role and project on Windows, without admin rights, and keeps them up to date. It is a C# (.NET 10) console app published as a single Native AOT executable for `win-x64`. This repository holds only the installer and contains no secrets.

Status: skeleton. Only `nexer-ai --version` works; `install`, `project`, `doctor`, `statusline` and `mcp-launch` come next.

## Layout

| Path | Contents |
|---|---|
| `src/NexerAI.Core` | Domain types, ports and the logic built on them. No I/O, no process calls, AOT compatible |
| `src/NexerAI.Adapters` | Port implementations that touch the machine (file system, processes, network), AOT compatible |
| `src/NexerAI.Cli` | `nexer-ai.exe`, the composition root that wires adapters to the core |
| `tests/NexerAI.Core.Tests` | xUnit v3 tests for the core, against hand-written fakes of the ports |
| `tests/NexerAI.Adapters.Tests` | xUnit v3 tests for the adapters, against real temporary directories |
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

## Profile resolution

`ProfileResolver` (`src/NexerAI.Core/Profiles`) normalizes every remote of the working copy, not only `origin`, and matches the keys against the `repos` of the profiles on the person's channel. Profile `repos` hold repository URLs, and both sides go through the same normalizer. The result is `Matched` (exactly one profile), `Unmatched` (the unknown-repository flow) or `Ambiguous` (several profiles, ordered by id for the error message). Every case carries the distinct remote keys for the profile request issue.

## Stack detection

`FileSystemStackDetector` (`src/NexerAI.Adapters/Stacks`) implements `IStackDetector` by walking the working copy. It skips `.git`, `bin`, `obj` and `node_modules` folders (not `packages`, which holds the sources of JavaScript monorepos), does not follow junctions or symbolic links, and ignores entries it cannot read. The signals, reported at most once each and in this order:

| Signal | Found by | Suggested plugin |
|---|---|---|
| `Umbraco` | A `.csproj` with a `PackageReference` to `UmbracoCms` (Umbraco 8) or `Umbraco.Cms` / `Umbraco.Cms.*` (9 and later) | `nexer-dev-umbraco` |
| `Playwright` | A `playwright.config.*` file | None; noted in the profile request issue for the QA role |
| `Cypress` | A `cypress.config.*` file | None; noted in the profile request issue for the QA role |

The rules live in one table in the detector. A new signal is one entry there, added together with the plugin it suggests.

## Desired state and plan

`DesiredState` (`src/NexerAI.Core/Planning`) lists the plugins and MCP servers a command wants present, all from the person's channel (the marketplace name). `ForMachine` covers `install`: `nexer-core`, the role plugin (`nexer-qa` for QA, `nexer-po` for PM/PO) and the opt-in `nexer-mod`, at user scope. `ForProject` covers `project`: the profile's stack plugins and `nexer-engram` when the profile uses it, at local scope, plus one local MCP server `nexer-<type>` per tracker that runs `<bin>\nexer-ai.exe mcp-launch --type <type> --url <url>` with no secret in it.

`InstallPlanner` diffs it against what the agent reports and returns `InstallPlugin` and `AddMcpServer` actions, so a second run only fixes drift. A plugin counts as present when the same name, marketplace and scope is installed, whatever its version; an MCP server when the same name and scope is registered. Removing what is no longer wanted (it needs the state file), switching channel, credentials, plugin configuration values and drift in a server's command or arguments are not handled yet.

## Build and test

Requires the .NET SDK pinned in `global.json`. Tests run on Microsoft.Testing.Platform (configured in `global.json`).

```powershell
dotnet build
dotnet test
```

Native AOT publish (`dotnet publish src/NexerAI.Cli -c Release -r win-x64`) needs the Visual Studio C++ build tools; without them it fails with "Platform linker not found". CI publishes the AOT executable and runs `nexer-ai.exe --version` on every push and pull request, so locally `build` and `test` are enough.
