# Pubwiz — research & implementation plan

`dotnet pubwiz` is an interactive publish wizard for .NET projects: folder, IIS Web Deploy, Zip/One deploy, FTP/FTPS/SFTP, Azure (App Service, WebJobs, Functions, Container Apps), container images (SDK or Dockerfile → local daemon, ACR/any registry, tarball) and MAUI (Android, iOS). It wraps `dotnet`, `dotnet msbuild`, `az` and `docker`/`podman`, and adds a few native clients where no CLI exists (FTP/SFTP, Kudu WebJobs, Web Deploy).

## Status (2026-10-31)
- ✅ **Phase 0, scaffold:** solution, shared props, central packages, CI workflows, dotnet tool + standalone exe.
- ✅ **Phase 1, discovery & profiles:** workspace scan (sln/slnx → csproj → 2 levels deep), MSBuild evaluation, project kinds, `.pubxml` read/write, `.publishsettings` import, project picker (Enter without selecting = single project), profile menu, Folder profile creation.
- ✅ **Phase 2, execution engine:**
  - Jobs made of steps, sequential or parallel (`--parallel`).
  - Live checklist with an optional log tail (`--show-log`, toggle with L).
  - Ctrl+C cancels and kills the build process tree.
  - Results table; log/JSON/CSV export.
  - OS keychain secret store: libsecret, macOS Keychain, Windows Credential Manager.
  - Folder target.
- 🟡 **Phase 3, Web Deploy (on-premises IIS):**
  - ✅ Windows: the `WebDeploy` target stages the publish, then runs `msdeploy.exe -verb:sync` itself (Basic or NTLM, `-allowUntrusted`, `DoNotDeleteRule`, `AppOffline`). The generated command was verified against the VM (site root and a new `my app` application).
  - ✅ Azure Web Deploy profiles deploy through Kudu on every OS.
  - ⏸ Linux/macOS to on-prem IIS: the custom client in `Pubwiz.WebDeploy` isn't written (only endpoint rules, error decoding and protocol notes).
  - ✅ Without msdeploy.exe, Web Deploy stays selectable with a warning. Creating one, or picking an existing on-prem profile, suggests FTP/FTPS, SFTP or a Zip file for the same server (FTP/SFTP prefilled with its host and user name); Azure App Service is suggested while the server is unknown. Keeping Web Deploy is allowed (for Windows or CI).
  - ✅ New Zip file target: a FileSystem profile with `PubwizZipFile` (Visual Studio still sees a folder profile).
  - msdeploy.exe parses its raw command line itself: every value is wrapped in `"…"` (keeps spaces and commas), and values with `"`, `'` or a trailing `\` are rejected because msdeploy can't receive them. The password is on msdeploy's command line (it has no stdin or file option).
- ✅ **Phase 4, Azure:**
  - App Service and Function Apps (Kudu one-deploy), WebJobs (run script, settings.job schedule).
  - Sign-in with az tokens or publishing credentials; az install, login and subscription in the wizard.
  - Not tested against real Azure.
- ✅ **Phase 5, FTP/FTPS/SFTP:** SFTP verified on the VM; FTP not verified.
- ✅ **Phase 6, Containers:**
  - Built with the SDK or a Dockerfile, sent to local Docker/Podman, a tarball, a registry, or ACR + App Service/Container Apps.
  - SDK and Dockerfile builds verified with podman. Registry pushes and Azure updates not verified.
- ✅ **Phase 7, web.config transforms:** `dotnet pubwiz transform`.
- ✅ **Phase 8, MAUI:**
  - Android (signing via `env:` passwords, keystore creation) and iOS (macOS).
  - No real build run: the workloads aren't installed here.
- ✅ **Phase 9, presets + CI:**
  - `pubwiz.json` presets: save from the wizard, `--preset`, `--preset-file`, `presets list/export`.
  - `--project/--profile`, `--yes`, env var passwords, `--report`/`--log-dir`, never prompts without a terminal.
- 🧪 **Test VM** (`tools/test-vm`): IIS + Web Deploy + SSH (key only).

Finding: `dotnet publish -p:PublishProfile=<MSDeploy profile>` fails outside Windows ("msdeploy.exe is invalid"). Upload targets therefore publish with the profile plus `-p:WebPublishMethod=FileSystem` to a staging folder (`obj/pubwiz/<profile>/`), then upload it themselves.

---

## 1. Research findings

### 1.1 Tool packaging / invocation
- To get `dotnet pubwiz`, the tool's `ToolCommandName` must be **`dotnet-pubwiz`** (same trick as `dotnet-ef`). The `dotnet` host resolves `dotnet <x>` to `dotnet-<x>` on PATH (global tools live in `~/.dotnet/tools`) and to local-manifest tools too. .NET 10 also allows `dnx pubwiz` one-shot runs.
- Same layout as AspireRunner: `Pubwiz.Tool.csproj` (PackAsTool) + `Pubwiz.Tool.Standalone.csproj` (single-file, R2R, self-contained per RID) + `Pubwiz.Core`. Only `net10.0` (per your request), `RollForward=Major`.

### 1.2 Project discovery
- `.sln` and `.slnx` parsing: **`Microsoft.VisualStudio.SolutionPersistence`** (the library the dotnet CLI itself uses for slnx). Handles solution folders and both formats.
- Project facts (TFMs, OutputType, SDK, UseMaui, AzureFunctionsVersion, IsAspireHost, IsPublishable, EnableSdkContainerSupport, ContainerRepository, etc.) come from **`dotnet msbuild <proj> -getProperty:A -getProperty:B ... -getItem:...`**, which prints JSON (SDK 8+). That way we skip MSBuildLocator and get evaluated values that respect Directory.Build.props. It runs in parallel for many projects, with a cheap XML pre-scan (Sdk attribute) for instant display.
- Discovery order (what you specified): sln/slnx in cwd → csproj in cwd → repeat both checks down to 2 levels deep → nothing found = error. If several solutions are found, let the user pick one.

### 1.3 Microsoft.NET.Sdk.Publish (WebSdk) — what `dotnet publish` already does
Read from `dotnet/sdk/src/WebSdk/Publish`:

| `WebPublishMethod` | Works cross-platform? | Notes |
|---|---|---|
| `FileSystem` | ✅ | `PublishUrl`/`PublishDir`, `DeleteExistingFiles` |
| `ZipDeploy` | ✅ | POSTs to `{PublishUrl}/api/zipdeploy` with Basic auth (`UserName`/`Password`) |
| `OneDeploy` | ✅ | POSTs to `/api/publish`; for WebJobs it PUTs to `/api/continuouswebjobs/{name}` or `/api/triggeredwebjobs/{name}` |
| `MSDeploy` | ❌ **Windows only** | The `MSDeploy` task is a wrapper around `msdeploy.exe` (path comes from the registry); `Microsoft.Web.Deployment` is only referenced for .NET Framework |
| `Package` | ❌ Windows only | Makes a Web Deploy package (msdeploy) |
| Container (`/t:PublishContainer`) | ✅ | see 1.6 |

- **web.config transforms** run as part of publish, in this order: `web.$(Configuration).config` → `web.$(PublishProfileName).config` → `web.$(EnvironmentName).config` → `Properties/PublishProfiles/<Profile>.transform`. Turned off by `IsTransformWebConfigDisabled=true`; Azure tweaks apply when `PublishProvider=AzureWebSite`. The SDK uses **`Microsoft.Web.Xdt`** (netstandard, cross-platform). We can use the same package for the standalone "just transform web.config" flow, so the output matches what publish would produce.

### 1.4 Web Deploy (MSDeploy) without Windows
- No open-source cross-platform msdeploy client exists (searched GitHub/npm/PyPI; every wrapper is Windows-only). JetBrains Rider also declined to build one. The WMSVC protocol (`https://host:8172/msdeploy.axd?site=...`, `Content-Type: application/msdeploy`, `MSDeploy.Method`/`VersionMin`/`VersionMax`/`RequestId` headers) is **undocumented**, so a client would have to be reverse-engineered.
- Proposed strategy, in layers:
  1. **Azure App Service MSDeploy profiles** (most `.publishsettings` imports): the same site + credentials also work against Kudu, so on any OS we silently run them as **ZipDeploy/OneDeploy** against `https://<site>.scm.azurewebsites.net` and tell the user. Same files end up deployed, with no msdeploy involved.
  2. **Windows + msdeploy.exe installed**: let the SDK do it (`dotnet publish /p:PublishProfile=X /p:Password=...`).
  3. **On-prem IIS from Linux/macOS**: our own **`Pubwiz.Core.WebDeploy` client** for the WMSVC sync protocol. Built in a late phase as **experimental**, after a spike that captures real `msdeploy.exe` traffic against a test IIS server. Until then we offer FTP/SFTP (+ `app_offline.htm`) as a fallback for IIS hosts.

### 1.5 Azure
- **Zip/One deploy (Kudu publish API)**: `POST https://<scm>/api/publish?type=zip[&restart=&clean=&path=]`. Auth is either **Entra bearer token** (`az account get-access-token`) or **Basic** with deployment credentials from publishsettings. The package limit is 2 GB. Basic auth (SCM) is **off by default on new apps**, so imported publishsettings may not work; then we fall back to az + bearer token.
- `az webapp deploy --src-path x.zip --type zip` wraps the same API but gives no upload progress.
- **WebJobs**: there is no `az` command for uploading, only Kudu (`PUT /api/continuouswebjobs/{name}` | `triggeredwebjobs`, zip body). The SDK's OneDeploy WebJob path does exactly this.
- **Functions**: Kudu zipdeploy (`az functionapp deployment source config-zip`); Flex Consumption uses OneDeploy `/api/publish`.
- **Container targets**: `az acr login -n <reg> --expose-token` → token for the SDK container push (`DOTNET_CONTAINER_REGISTRY_UNAME=00000000-0000-0000-0000-000000000000`, `..._PWORD=<token>`), so no docker daemon is needed. Then `az webapp config container set` (App Service container) or `az containerapp update/create --image` (Container Apps).
- **Discovery**: `az account list`, `az account set`, `az webapp list` (filter `kind`: `app`, `app,linux`, `app,linux,container`, `functionapp*`), `az acr list`, `az containerapp list`, `az webapp deployment list-publishing-profiles --xml` (generates a publishsettings for an existing app).
- **Install az CLI**: Windows `winget install -e --id Microsoft.AzureCLI`; macOS `brew install azure-cli`; Linux, detected from `/etc/os-release`: apt script, `dnf`, `zypper`, `tdnf`. Immutable distros (Fedora Atomic/Bluefin, `rpm-ostree`) → suggest Homebrew. We show the exact command and run it only after the user confirms (it needs sudo, so it runs in the foreground with inherited TTY).
- **Auth**: `az account show` to check → `az login` (browser), or `az login --use-device-code` when headless/SSH/WSL → choose a subscription (`az account set`). Tenant selection via `--tenant`.

`.publishsettings` format (VS "Import profile"): `<publishData><publishProfile profileName publishMethod="MSDeploy|FTP|ZipDeploy" publishUrl msdeploySite userName userPWD destinationAppUrl ftpPassiveMode .../></publishData>`. One file has several profiles; the wizard lets the user pick which ones to import and turns each into a `.pubxml`.

### 1.6 Containers
- **SDK way**: `dotnet publish -t:PublishContainer` with `ContainerRegistry`, `ContainerRepository`, `ContainerImageTag(s)`, `ContainerBaseImage`/`ContainerFamily`, `ContainerRuntimeIdentifier(s)` (multi-arch → OCI index), `ContainerArchiveOutputPath` (**tar.gz tarball**, no daemon needed), `LocalRegistry=Docker|Podman`. Auth uses docker credential helpers or the `DOTNET_CONTAINER_REGISTRY_UNAME/PWORD` env vars.
- **Dockerfile way**: `docker|podman build -f <Dockerfile> -t <img> <context>` → `push` or `save -o x.tar`. The build context defaults to the solution dir (VS-style Dockerfiles `COPY` paths relative to it).
- Container tool detection: `docker` / `podman` on PATH, `docker` being a podman alias, and whether the daemon/socket is reachable (`docker info`).

### 1.7 MAUI
- **Android**: `dotnet publish -f net10.0-android -c Release -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=… -p:AndroidSigningKeyAlias=… -p:AndroidSigningKeyPass=env:PUBWIZ_KS_PASS -p:AndroidSigningStorePass=env:PUBWIZ_KS_PASS -p:AndroidPackageFormats=aab|apk`. Use the `env:` prefix so passwords never appear in logs. The wizard can also create a keystore through `keytool` (JDK). Output goes to `bin/Release/net10.0-android/publish/*-Signed.aab/.apk`.
- **iOS**: `dotnet publish -f net10.0-ios -p:ArchiveOnBuild=true -p:RuntimeIdentifier=ios-arm64 -p:CodesignKey=… -p:CodesignProvision=…` → `.ipa`. **macOS only**: we hide or disable it elsewhere with a clear reason. We list signing identities via `security find-identity -v -p codesigning` and provisioning profiles from `~/Library/MobileDevice/Provisioning Profiles`.
- Others that could come cheaply later: Windows (`net10.0-windows10.0.19041.0`, MSIX/unpackaged), Mac Catalyst.
- Never publish the whole solution for MAUI (docs warn about this); always publish the project with an explicit `-f`.

### 1.8 Libraries
| Need | Package |
|---|---|
| CLI parsing, prompts, live rendering | `Spectre.Console`, `Spectre.Console.Cli` (as AspireRunner) |
| sln/slnx | `Microsoft.VisualStudio.SolutionPersistence` |
| FTP/FTPS | `FluentFTP` (mirror/sync upload, progress callbacks) |
| SFTP | `SSH.NET` (`Renci.SshNet`) |
| XDT | `Microsoft.Web.Xdt` |
| Logging | `Microsoft.Extensions.Logging(.Abstractions)` + source-gen `[LoggerMessage]` |
| Versions | `SemanticVersioning` (as AspireRunner) |

**Spectre gap:** `MultiSelectionPrompt` doesn't expose the highlighted item when Enter is pressed with nothing selected. Your "Enter without Space = single-project mode" rule therefore needs a **custom prompt** (`ProjectPickerPrompt : IPrompt<ProjectPickResult>`) built on `IAnsiConsole.Input` + `Live`. That's a small, self-contained widget.

---

## 2. Architecture

```
Pubwiz.slnx
├─ src/
│  ├─ Pubwiz.Core/                   net10.0 class library (no Spectre dependency)
│  │  ├─ Abstractions/               IPublishTarget, IProcessRunner, ISecretStore, IPublishObserver
│  │  ├─ Helpers/                    ProcessHelper, PlatformHelper, EnvironmentVariables (ported from AspireRunner)
│  │  ├─ Cli/                        DotnetCli, MsBuildEvaluator, AzCli, ContainerCli, KeytoolCli, AppleSigningCli
│  │  ├─ Discovery/                  WorkspaceScanner, SolutionReader, ProjectInspector → ProjectInfo + ProjectKind
│  │  ├─ Profiles/                   PublishProfile model, PubxmlReader/Writer, PublishSettingsImporter
│  │  ├─ Targets/                    one folder per target (see §4), each = IPublishTarget
│  │  ├─ Azure/                      AzureSession (login/subscription), AzureResources, KuduClient
│  │  ├─ Transfer/                   FtpUploader, SftpUploader, ZipPackager (shared)
│  │  ├─ WebDeploy/                  (experimental) WMSVC client
│  │  ├─ Transforms/                 XdtTransformer, WebConfigTransformPlanner
│  │  ├─ Execution/                  PublishPlan, PublishRunner, PublishStep, StepStatus, PublishResult
│  │  └─ Reporting/                  PublishLog (in-memory, unbounded), LogExporter, ReportExporter (json/csv)
│  └─ Pubwiz.Tool/                   Exe, PackAsTool, ToolCommandName=dotnet-pubwiz
│     ├─ Program.cs                  CommandApp, default command = publish
│     ├─ Commands/                   PublishCommand, TransformCommand, ProfilesCommand, AzureCommand (login/logout/status)
│     ├─ Wizard/                     WizardFlow, per-target wizard pages (one class per target)
│     ├─ Prompts/                    ProjectPickerPrompt (custom), PathPrompt, SecretPrompt
│     ├─ Rendering/                  PublishDashboard (live steps + tail), Widgets(.operations).cs
│     ├─ Logging/                    InMemoryLogger(Provider) (ported)
│     └─ Pubwiz.Tool.Standalone.csproj
└─ tests/
   └─ Pubwiz.Core.Tests/             unit tests: discovery, pubxml/publishsettings parse+write, XDT, arg building
```

Key design points:
- **Core has no UI.** Targets report progress through `IPublishObserver` (step started/finished/failed, log line, upload progress). The Tool renders it. This keeps Core usable later from a non-interactive mode or another host.
- **Every external call goes through `IProcessRunner`.** It streams stdout/stderr line by line into the `PublishLog`, keeps the full transcript, and supports cancellation (Ctrl+C kills the process tree). Secrets are passed through env vars and **masked** in logged command lines and output.
- **A `PublishPlan` is pure data** (project + profile + resolved options). The wizard only builds plans; `PublishRunner` executes them. A batch is a list of plans.
- Profiles stay **VS-compatible**: we read and write `Properties/PublishProfiles/*.pubxml`. For targets VS has no MSBuild method for (FTP-by-pubwiz, SFTP, Container Apps…), we write standard properties plus `Pubwiz*` properties so VS ignores them harmlessly. Passwords are never written to `.pubxml` (see open question on secrets).

### 2.1 Publish execution pipeline (per project)
Each target gives an ordered list of steps; the dashboard shows them as a checklist with spinner / ✓ / ✕ / skipped and timings:
1. Validate prerequisites (tools on PATH, az logged in, docker reachable, signing assets present)
2. Restore + build + publish (`dotnet publish` with the profile/props) → publish dir
3. (Web) web.config transform — the SDK does it; we only surface it as a step and verify the result
4. Package (zip / container image / signed aab)
5. Authenticate (az token / registry token / basic)
6. Deploy / upload (with % progress for HTTP/FTP/SFTP)
7. Post-deploy (restart, poll Kudu deployment status, set container image, open site URL)

### 2.2 UI flow
```
dotnet pubwiz [path] [options]
  └─ discover → load projects (spinner) → ProjectInfo[]
      ├─ 1 project  → Single-project page
      └─ N projects → ProjectPickerPrompt (Space toggles, Enter confirms)
            ├─ ≥1 selected → Batch mode: for each selected project → Single-project page (collect plan only)
            └─ none selected → Single-project mode on the highlighted project
Single-project page:
  existing profiles (from Properties/PublishProfiles) + "Create new profile" + "Import profile (.publishsettings / .pubxml)"
  └─ Create → target picker filtered by ProjectKind → target wizard pages → summary → save as .pubxml? → plan
Run:
  summary table of all plans → confirm → live dashboard
     per project: step checklist; optional live log tail (toggle with L key or --show-log; last 10 lines)
  → results table (status, duration, output/URL)
  → post-run menu: export log(s) • export report (json/csv) • open output folder / site URL • exit
```

### 2.3 Target availability by project kind (VS-like)

| ProjectKind (detection) | Targets offered |
|---|---|
| **Web** (`Microsoft.NET.Sdk.Web`, not Blazor WASM) | Folder, IIS Web Deploy, Web Deploy package*, FTP/FTPS, SFTP, Azure App Service (Win/Linux), Azure App Service (container), Azure Container Apps, Container registry (ACR / Docker Hub / other), Local container, Container tarball |
| **Blazor WASM standalone** (`Sdk.BlazorWebAssembly`) | Folder, FTP/SFTP, Azure App Service, Azure Static Web Apps*, Container |
| **Worker** (`Microsoft.NET.Sdk.Worker`) | Folder, Azure WebJob (continuous), Azure Container Apps, Container registry/local/tarball, FTP/SFTP |
| **Console** (`OutputType=Exe`) | Folder, Azure WebJob (triggered/continuous), Container, FTP/SFTP |
| **Azure Functions** (`AzureFunctionsVersion` set) | Folder, Azure Function App (Win/Linux/Flex), Function App container / Container Apps, Container |
| **MAUI** (`UseMaui=true`) | Android (apk/aab, signing), iOS (ipa, macOS only), Folder |
| Library / test / Aspire AppHost | Hidden by default (shown with `--all`), not publishable |

`*` = later phase / stretch.

---

## 3. CLI surface (Spectre.Console.Cli, AspireRunner style)

```
dotnet pubwiz [path]                 # default: discover + wizard (PublishCommand)
   -c|--configuration <cfg>          # default Release
   -p|--profile <name>               # skip profile prompt when it exists (applies to all selected projects)
   --project <glob>                  # preselect projects (repeatable)
   --all                             # include non-publishable projects in lists
   --show-log                        # start with live log tail visible
   --log-lines <n>                   # tail length, default 10
   --parallel <n>                    # batch concurrency (default 1)
   --verbose
dotnet pubwiz transform [path]       # standalone web.config XDT flow
   --config <cfg> --profile <p> --environment <env> --transform <file> -o <out>
dotnet pubwiz profiles [path]        # list / import / delete profiles
dotnet pubwiz azure login|logout|status|subscription
```

---

## 4. Targets — implementation notes

| Target | How |
|---|---|
| **Folder** | `dotnet publish -p:PublishProfile=X` or `-o`; we handle `DeleteExistingFiles` |
| **Zip deploy (generic Kudu)** | `dotnet publish` → our ZipPackager → `KuduClient.PublishAsync(type=zip)` with progress + status polling; Basic or bearer |
| **Azure App Service** | az picks subscription/app → bearer token → Kudu publish (same code as above). Fallback: `az webapp deploy` |
| **Azure WebJob** | publish → zip with `run.cmd`/`run.sh` (as `GenerateRunCommandFile` does) → `PUT /api/{continuous|triggered}webjobs/{name}` |
| **Azure Functions** | publish → zip → Kudu zipdeploy, or `/api/publish` for Flex; `az functionapp` for discovery |
| **IIS Web Deploy** | Azure site → Kudu path; Windows + msdeploy.exe → staging publish, then our own `msdeploy -verb:sync` call (`MsDeployCli`); otherwise experimental WMSVC client |
| **FTP / FTPS** | publish → `FluentFTP` mirror upload (only changed files, optional delete of extra remote files), `app_offline.htm` up/down around the upload |
| **SFTP** | same with `SSH.NET` (password or private key) |
| **Container (SDK)** | `dotnet publish -t:PublishContainer` + props; ACR token via az; tarball through `ContainerArchiveOutputPath` |
| **Container (Dockerfile)** | `docker/podman build` → `push` / `save`; ACR login via `az acr login` (needs daemon) or token + `docker login --password-stdin` |
| **App Service container / Container Apps** | image push (above) → `az webapp config container set` / `az containerapp update --image` (create flow later) |
| **MAUI Android** | `dotnet publish -f net10.0-android` + signing props through `env:`; optional keystore creation via `keytool` |
| **MAUI iOS** | macOS only; pick identity + provisioning profile; `ArchiveOnBuild=true` |

---

## 5. Logging & reports
- `PublishLog` per project: an unbounded, append-only list of `LogEntry(Timestamp, Source[stdout|stderr|pubwiz], Level, Step, Text)`, kept in memory (no disk until you export). The UI tail reads the last N entries; it doesn't drain a channel the way AspireRunner's bounded logger does.
- Export: `pubwiz-<timestamp>/<project>.log` (or one combined file), plus `pubwiz-report-<timestamp>.json|csv` with: project, path, profile, target, configuration, TFM/RID, start, end, duration, status, exit code, failed step, error summary, artifact path/size, destination URL/image ref, deployment id.
- dotnet/msbuild output is captured with `-v:n` (or `-v:m` + `-clp:...`), plus `-bl` (binlog) when `--verbose`.

---

## 6. Phased delivery

| Phase | Scope |
|---|---|
| **0 – Scaffold** | slnx, Directory.Build.props, .editorconfig/.gitignore (from AspireRunner), Core + Tool + Standalone + tests, ported helpers, CI workflows |
| **1 – Discovery & profiles** | WorkspaceScanner, MsBuildEvaluator, ProjectKind, pubxml read/write, publishsettings import, custom ProjectPickerPrompt, single/batch wizard shell |
| **2 – Execution engine & UI** | ProcessRunner with streaming + masking, PublishRunner, live dashboard + log tail, result table, log/report export. **Folder target** end-to-end |
| **3 – Kudu & Azure** | az detection/install/login/subscription, KuduClient, Zip/One deploy, App Service, WebJobs, Functions; MSDeploy→Kudu conversion for Azure profiles |
| **4 – FTP/FTPS/SFTP** | FluentFTP + SSH.NET uploaders, `app_offline.htm`, publishsettings FTP profiles |
| **5 – Containers** | SDK container (local/registry/tarball/multi-arch), Dockerfile flow, ACR token auth, App Service container + Container Apps update |
| **6 – web.config transform command** | `pubwiz transform`, plus a wizard step that previews transforms in publish |
| **7 – MAUI** | Android (signing, keystore creation), iOS (macOS) |
| **8 – Web Deploy (experimental)** | traffic-capture spike → WMSVC client for on-prem IIS from Linux/macOS; Windows msdeploy.exe path |
| Later | Static Web Apps, create Azure resources from the wizard, non-interactive/CI mode, Windows/MacCatalyst MAUI |

---

## 7. Decisions (2026-10-31)

| Topic | Decision |
|---|---|
| Secrets | **OS keychain**: Windows Credential Manager (+ read VS `EncryptedPassword` via DPAPI), macOS Keychain (`security`), Linux libsecret (`secret-tool`). Prompt every run when none is available. Env vars always override (for CI). `ISecretStore` in Core, one implementation per OS. |
| Web Deploy | **Custom WMSVC client is a priority.** It moves to phase 3, right after the execution engine. You have an IIS + Web Deploy server to test against. The spike captures `msdeploy.exe` traffic (Windows client → your IIS) to document the protocol in `docs/webdeploy-protocol.md` before coding. Kudu conversion for Azure MSDeploy profiles and msdeploy.exe on Windows stay as the other layers. |
| Azure deploy | **az for auth/discovery/tokens + our own Kudu HTTP client for uploads** (progress + status polling; Basic auth for imported publishsettings). |
| v1 extras | **Parallel batch publish** (`--parallel N`, default 1), **non-interactive/CI mode**, **batch presets** (`pubwiz.json` next to the sln). Creating Azure resources is **not** in v1. |

### Revised phases
0 Scaffold → 1 Discovery & profiles → 2 Execution engine, UI, Folder target, reports → **3 Web Deploy (spike + WMSVC client)** → 4 Kudu & Azure (App Service, WebJobs, Functions) → 5 FTP/FTPS/SFTP → 6 Containers → 7 transform command → 8 MAUI → 9 non-interactive mode + batch presets polish.

Non-interactive mode shape: `dotnet pubwiz --project <name|glob> --profile <name> [--preset <name>] --yes [--report json|csv --log-dir <dir>]`. It fails fast (non-zero exit) when anything would need a prompt. Interactive mode is turned off automatically when stdin isn't a TTY or `CI=true`.

Batch preset (`pubwiz.json`):
```json
{ "presets": { "prod": { "configuration": "Release", "parallel": 2,
    "projects": [ { "path": "src/Api/Api.csproj", "profile": "Prod-AppService" },
                  { "path": "src/Worker/Worker.csproj", "profile": "Prod-WebJob" } ] } } }
```

## 8. Smaller defaults (assumed unless changed)
- Unit tests with **xUnit v3** in `tests/Pubwiz.Core.Tests`.
- Package metadata the same as AspireRunner: author SaifAqqad, **Unlicense**, nupkg to `dist/nupkg`, GitHub workflows for NuGet + standalone executables.
- `.editorconfig` copied from AspireRunner (CRLF, UTF-8 BOM, 4-space indent).
- Central package management (`Directory.Packages.props`) + `Directory.Build.props` for shared metadata (AspireRunner repeats metadata per csproj; this is a small deviation).
- Profiles created in the wizard are always saved as `.pubxml` (you're asked for the name). The secret goes to the keychain, never to the file.
- Aspire AppHost, test and library projects are hidden unless `--all` is passed.
- Azure Static Web Apps and MAUI Windows/MacCatalyst come after v1.
