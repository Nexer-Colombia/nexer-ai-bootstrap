# nexer-ai-bootstrap

Installer for the Nexer Colombia AI adoption program. `nexer-ai` installs Claude Code plus the plugins and MCP servers approved for a person's role and project on Windows, without admin rights, and keeps them up to date. It is a C# (.NET 10) console app published as a single Native AOT executable for `win-x64`. This repository holds only the installer and contains no secrets.

Status: skeleton. `nexer-ai --version` and `nexer-ai statusline` work; `install`, `project`, `doctor` and `mcp-launch` come next.

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
| `IStateStore` | The per-user state file: read, and read-modify-write as one exclusive update |
| `IReadOnlyFileSystem` | Folder and file checks and UTF-8 reads, for example of the git files behind a task key |
| `ISessionCacheStore` | The status line's memory of each session: load, save, delete and sweep; never throws |

## Test vectors

`test-vectors/remotes.json` defines how a git remote is normalized to the canonical `host/path` key that `nexer-ai project` matches against the `repos` of project profiles. Each case has an `input`, the `expected` key (`null` means the input must be rejected) and a `note` with the reason.

`test-vectors/task-keys.json` defines the task key `<repository root>#<branch>` of a working directory, which the Nexer mod uses to add up agent time per task. Each case has a `name`, a Windows file tree in `files` (paths match case-insensitively and every parent of a file is a folder), the `cwd`, and the `expected` key with its `sha256` (the task file name), or `null` when the directory has no task.

The files are contracts. Marketplace CI applies the remote rule to reject two profiles that list the same remote, the Nexer mod will use both files and the task skill the task keys; every implementation must pass every case. This repository is the only source: consumers download a file pinned to a bootstrap tag, never to a branch:

```text
https://raw.githubusercontent.com/Nexer-Colombia/nexer-ai-bootstrap/<tag>/test-vectors/remotes.json
```

Changing a vector needs a pull request here (with the matching code change) and a new tag; consumers then move their pin. The xUnit theories in `tests/NexerAI.Core.Tests/Remotes` and `tests/NexerAI.Core.Tests/Tasks` run every case, so a new vector needs no test code.

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

`InstallPlanner` diffs it against what the agent reports and returns `InstallPlugin` and `AddMcpServer` actions, so a second run only fixes drift. A plugin counts as present when the same name, marketplace and scope is installed, whatever its version and even when the person disabled it (installing it again would enable it); an MCP server when the same name and scope is registered. Removing what is no longer wanted (it needs the state file), switching channel, credentials, plugin configuration values and drift in a server's command or arguments are not handled yet.

## Reading Claude Code's state

`ClaudeCodeAgentInstaller` (`src/NexerAI.Adapters/ClaudeCode`) implements the read side of `IAgentInstaller` for one working directory, verified on Claude Code 2.1.300 (design 4.4). Registering MCP servers comes in a later unit and throws `NotImplementedException` for now.

- Plugins: it runs `claude plugin list --json` in the working directory through `IProcessRunner` (`src/NexerAI.Adapters/Processes`; `claude.exe` from the native installer, found on `PATH`) and `ClaudePluginList` keeps user-scope plugins and the local-scope plugins whose `projectPath` is that exact folder: a local plugin installed in a subfolder does not apply at the repository root, nor the reverse. Other scopes and ids are skipped. `enabled` is kept, and a disabled plugin still counts as installed ([JSON output](https://code.claude.com/docs/en/plugins/cli-reference#json-output)).
- MCP servers: `claude mcp list` and `get` start the servers to check them ([MCP](https://code.claude.com/docs/en/mcp)), so `ClaudeConfig` reads `.claude.json` instead (`$CLAUDE_CONFIG_DIR\.claude.json`, else `%USERPROFILE%\.claude.json`): the top-level `mcpServers` (user scope, observed) and `projects["<key>"].mcpServers` (local scope, documented). The key is the repository root with forward slashes, found like the task key, or the working directory outside a repository; keys match ignoring case and separators. Only names and scopes are read, never `env` or `headers`. A missing file means no servers.
- Errors: a `claude` that cannot start or fails, or output or a file with a shape it does not recognize, raise `AgentStateException`, whose message never quotes the output or the file. `doctor` will show it as a warning.

The parsers are tested against samples recorded with an isolated `CLAUDE_CONFIG_DIR` (`tests/NexerAI.Core.Tests/ClaudeCode/Samples`).

## Changing Claude Code's state

Every `claude` command runs in the adapter's working directory, which `nexer-ai project` sets to the repository root: a local-scope plugin belongs to that exact folder.

- Plugins: `claude plugin install <name>@<marketplace> --scope <scope> --json`, plus one `--config key=value` per configuration value, ordered by key. Those values are on the command line, so `PluginInstallRequest.Config` holds non-secret values only; secrets will go through `claude plugin configure --values-stdin`. `ClaudePluginResult` reads the result line, the last line of stdout ([JSON result format](https://code.claude.com/docs/en/plugins/cli-reference#plugin-json-result)). Outcome `ok` is a success, including a plugin already installed at that scope. Outcome `failed`, another exit code than 0, no result line, or `configApplied: false` raise `AgentStateException`. `configApplied: false` is what Claude Code 2.1.300 reports, with outcome `ok` and exit code 0, for a `--config` key the plugin does not declare (observed, not documented). The message names the plugin and the `failureCode`, never the output.

## State file

`%USERPROFILE%\.nexer-ai\state.json` records what the bootstrap installed: the channel (marketplace name), the role token, the user-scope plugin versions and, per project keyed by normalized remote, the matched profile and the local plugins and MCP servers. It never holds secrets.

```json
{
  "schema": 1,
  "channel": "nexer",
  "role": "qa",
  "installed": { "nexer-core": "1.2.0", "nexer-qa": "0.4.0" },
  "projects": {
    "github.com/acme/website": { "profile": "acme-website", "plugins": ["nexer-dev-umbraco", "nexer-engram"], "mcp": ["nexer-azure-devops"] }
  }
}
```

`StateFileFormat` (`src/NexerAI.Core/State`) reads and writes schema 1 with source-generated System.Text.Json: indented, LF line ends, keys sorted ordinally, unknown properties ignored, missing sections read as empty. A missing or different `schema`, malformed JSON, duplicate properties, an unknown channel or role, or a null entry raise `StateFileException`; the file is never silently reset.

`FileStateStore` (`src/NexerAI.Adapters/State`) implements `IStateStore`. A missing file reads as the empty state. Two terminals may run `nexer-ai project` at once, so every update holds an exclusive lock on `state.json.lock` (kept between runs), reads the file, applies the change, writes a temporary file in the same folder, flushes it to disk and moves it over `state.json`. Waiting for the lock, and for other programs to close the file, is bounded (10 seconds by default) and honors cancellation. Reads take no lock: a reader sees the old or the new file, never half of one. The file is UTF-8 without BOM.

## Credentials

`DpapiCredentialStore` (`src/NexerAI.Adapters/Credentials`, Windows only) implements `ICredentialStore`: one file per tracker in `%USERPROFILE%\.nexer-ai\credentials\`, encrypted with DPAPI for the current user. The tracker URL is normalized (lowercase scheme, host and path, no default port, no trailing slash) and the file is named `<sha256 of the normalized URL>.dpapi`, so neither the URL nor the secret is stored in plain text. The DPAPI entropy binds each file to its URL: a file copied over another tracker's entry does not decrypt. A URL with user info, a query or a fragment is rejected without being echoed. A file that does not decrypt raises `CredentialStoreException` with its path and is left untouched; no message ever contains the secret. Writes go to a temporary file that is moved over the entry. DPAPI does not stop a process running as the same user, so the baseline denies Claude reads of the folder.

## Task key

The Nexer mod adds up agent time per task, and a task is a branch of a working copy: `<repository root>#<branch>`, for example `c:/work/acme-website#feature/12345-header`. Both are local values that never leave the machine. `TaskKeyResolver` (`src/NexerAI.Core/Tasks`) computes it by reading git files through `IReadOnlyFileSystem`, without starting git:

- The root is the first folder, walking up from the working directory, that contains `.git`, either a folder with a `HEAD` file (like git, a `.git` folder without one is skipped) or a file (worktree, submodule) whose `gitdir:` line names the git folder; a relative `gitdir:` starts at the folder of the `.git` file. Drive (`c:/`) and UNC (`//server/share`) paths are accepted, lowercased with forward slashes.
- The branch comes from `HEAD` in the git folder, `ref: refs/heads/<branch>`, with its case kept. A detached HEAD, a ref outside `refs/heads/` (`git branch --show-current` prints nothing), the reftable placeholder `.invalid` and a missing or unreadable file give no key.

`TaskKey.Sha256`, the lowercase hex SHA-256 of the UTF-8 key, names the task file `%USERPROFILE%\.nexer-ai\tasks\<sha256>.json`.

`LocalFileSystem` (`src/NexerAI.Adapters/FileSystem`) implements `IReadOnlyFileSystem` on the real file system; a file that is missing, a folder, locked or denied reads as `null`.

## Status line

`nexer-ai statusline` is a Claude Code status line command: it reads the status line JSON on stdin and prints one line with the prompt cache time left. It shows only the cache because the Nexer mod draws the other live metrics and the mods API does not expose the cache. `StatusLineInputParser` (`src/NexerAI.Core/StatusLine`) reads `prompt_cache` and never throws; `CacheTimerLine` formats it against the current time:

| Input | Output | Color |
|---|---|---|
| Unreadable, or no `prompt_cache` (before the first response) | `◷ cache -` | gray |
| Not `warm`, `expires_at` null, or `expires_at` reached | `◷ cache cold` | gray |
| Warm, 4 minutes left, `ttl` `5m` | `◷ cache 4m left (5m)` | green |
| Warm, 51 minutes 30 seconds left, `ttl` `1h` | `◷ cache 52m left (1h)` | green |
| Warm, 50 seconds left, `ttl` `5m` | `◷ cache 1m left (5m)` | yellow |
| Warm, last response 10 minutes ago, idle renewal seen, `ttl` `5m` | `◷ cache ~55m left (5m)` | green |
| Warm, last response 2 minutes ago, no renewal seen, `ttl` `5m` | `◷ cache 3m left (5m)` | green |

The time left is rounded up to whole minutes. A warm cache turns yellow when the exact time left is at most the warning window: 20% of the ttl, clamped to 60 to 300 seconds (`5m` gives 60 seconds, `1h` gives 300), or 60 seconds when the ttl is unknown. Colors are ANSI codes (`32` green, `33` yellow, `90` gray) and each line ends with a reset; when `NO_COLOR` is set and not empty ([no-color.org](https://no-color.org)) the same text is printed without them. Output is UTF-8 without a BOM. The ttl is shown only when it is one to three digits and `s`, `m` or `h`, so no other input text reaches the terminal. The command always exits 0. Claude Code re-runs it when a warm cache reaches `expires_at`; `install` will set `refreshInterval` for the countdown in between.

### Estimate after an idle renewal

Observed on Claude Code 2.1.300 and not documented: while a session is open and idle, `prompt_cache.expires_at` moves forward every 30 seconds with `prompt_cache.requests` unchanged, for 60 minutes after the last response, and the cache goes cold one ttl later. A plain countdown would read about 5 minutes for an hour. So the command remembers, per session (`CacheSession`, `src/NexerAI.Core/StatusLine`):

- the request count (zero before the first response, so the first response is a change), and the time of the last response: `expires_at` minus the ttl at the first reading after the count changed. When the command first sees a session after a response (first run after an install or update, or a deleted file), that reading may already be a renewal, so the last response stays unknown and the countdown exact until the next response;
- whether a renewal was seen: the same count with `expires_at` more than 5 seconds past the last response plus the ttl.

Estimate, then correct. With a known last response, and until 60 minutes after it, the line shows an estimate marked with `~`, counting to the last response plus 60 minutes plus the ttl, never less than the exact time left; it takes the same colors. The estimate shows right after the response: the cache is then warm for at least one ttl anyway, so a wrong estimate in the first 75 seconds cannot mislead. When more than 75 seconds have passed since the response and no renewal was seen, Claude Code has stopped renewing (they come every 30 seconds), so the line shows the exact countdown, and the estimate comes back if a renewal shows up later. With an unknown last response, after the 60 minutes, and for a cold or unknown cache, the line is exact as before.

### Session state and cleanup

`FileSessionCacheStore` (`src/NexerAI.Adapters/StatusLine`) keeps the memory in `%USERPROFILE%\.nexer-ai\statusline\<session_id>.json`, for example `{"requests":3,"lastResponseAt":1791997772,"renewalSeen":true}`. The `session_id` becomes a file name only when it is 1 to 128 ASCII letters, digits, `-` and `_`, and not a Windows device name (`CON`, `COM1`...); for any other id nothing is stored. The file is written only when the memory changes, through a temporary file moved over it. Every file error is ignored: the line still prints, with the exact countdown when the memory is missing.

Two cleanups keep the folder small:

- `nexer-ai statusline --session-end` reads the `SessionEnd` hook JSON on stdin, deletes that session's file, prints nothing and exits 0. `install` (M5) will register it as a `SessionEnd` command hook in user settings, next to the status line. `SessionEnd` runs when a session ends, with the reasons `clear`, `resume`, `logout`, `prompt_input_exit` and `other`, and its hooks share a 1.5 second budget ([hooks](https://code.claude.com/docs/en/hooks#sessionend)).
- The docs do not say `SessionEnd` runs when the terminal is closed, so every status line run also deletes the `*.json` files directly in that folder last written more than 2 hours ago. It never enters subfolders or follows links. After 65 idle minutes the cache is cold anyway.

## Build and test

Requires the .NET SDK pinned in `global.json`. Tests run on Microsoft.Testing.Platform (configured in `global.json`).

```powershell
dotnet build
dotnet test
```

Native AOT publish (`dotnet publish src/NexerAI.Cli -c Release -r win-x64`) needs the Visual Studio C++ build tools; without them it fails with "Platform linker not found". CI publishes the AOT executable and runs `nexer-ai.exe --version` on every push and pull request, so locally `build` and `test` are enough.
