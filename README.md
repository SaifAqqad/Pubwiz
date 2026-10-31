# Pubwiz

An interactive publish wizard for .NET: `dotnet publish`, but you pick the projects and targets from a menu.

> **Status:** early development.

Targets:
- Folder or Zip file
- IIS Web Deploy on Windows (runs the official `msdeploy.exe`)
- FTP/FTPS/SFTP
- Azure App Service, Function Apps and WebJobs (Kudu zip deploy, including imported `.publishsettings` Web Deploy profiles)
- Container images: built with the .NET SDK or a Dockerfile, sent to local Docker/Podman, a tarball, any registry, or ACR plus App Service or Container Apps
- .NET MAUI: Android `.aab`/`.apk` and iOS `.ipa`

IIS Web Deploy from Linux and macOS (a cross-platform client in `src/Pubwiz.WebDeploy`) is in progress. Until then, choosing Web Deploy there suggests FTP/FTPS, SFTP or a Zip file for the same server (or Azure App Service for Azure apps), and you can still create Web Deploy profiles to publish from Windows or CI.

## Commands

```bash
dotnet pubwiz [path]                      # pick projects and profiles, then publish (--parallel, --show-log, -c)
dotnet pubwiz --preset prod               # run a saved batch from pubwiz.json
dotnet pubwiz transform [path]            # write a transformed web.config without publishing
dotnet pubwiz presets list                # show saved presets
dotnet pubwiz presets export prod         # write one preset to its own file (run with --preset-file)
```

## Presets

Presets are saved only when you ask: pick "Save a profile as a preset" in a project's profile list, or, when publishing several projects, "Save this selection as a preset" before publishing. They go in `pubwiz.json`, next to your solution. Commit that file so the whole team (and CI) can publish the same batch:

```json
{
  "presets": {
    "prod": {
      "configuration": "Release",
      "parallel": 2,
      "projects": [
        { "path": "src/Api/Api.csproj", "profile": "Prod" },
        { "path": "src/Worker/Worker.csproj", "profile": "Prod-WebJob" }
      ]
    }
  }
}
```

## CI / non-interactive use

Without a terminal (or with `CI=true`), Pubwiz never prompts. It needs a preset or explicit projects:

```bash
dotnet pubwiz --preset prod --yes --report artifacts/publish.json --log-dir artifacts/logs
dotnet pubwiz --project Api --project Worker --profile Staging --yes
```

- Passwords come from `PUBWIZ_PASSWORD_<PROFILE>` (profile name in upper case, other characters as `_`), then `PUBWIZ_PASSWORD`, then the OS keychain.
- Azure profiles sign in with `az`; use `az login --service-principal` (or a workload identity) in CI.
- Web Deploy profiles without a user name sign in as the current Windows account. msdeploy.exe takes the password on its command line, so other users of the machine could see it while it runs.
- Exit codes: `0` success, `1` a publish failed, `2` a required tool is missing, `3` invalid input, `130` cancelled.

## Install

```bash
dotnet tool install -g Pubwiz
dotnet pubwiz
```

## Repository layout

| Path | Contents |
|---|---|
| `src/Pubwiz.Core` | Publishing logic, organized along a publish: discovery, profiles, the publishing engine, one folder per target, reporting ([layout guide](src/Pubwiz.Core/README.md)) |
| `src/Pubwiz.Tool` | The `dotnet pubwiz` command line app (Spectre.Console UI) |
| `src/Pubwiz.WebDeploy` | Standalone cross-platform Web Deploy client, published as its own NuGet package |
| `tests/*` | Unit and integration tests (xUnit v3) |
| `tools/test-vm` | A local Windows + IIS + Web Deploy VM for testing |
