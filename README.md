<p align="center">
  <img src="assets/logo.svg" width="128" height="128" alt="Pubwiz logo: a wizard's hat with an arrow launching from its tip">
</p>

<h1 align="center">Pubwiz</h1>

<p align="center">
  <b>The publish wizard for .NET.</b><br>
  Pick your projects and where they go. Pubwiz does the rest.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/Pubwiz"><img src="https://img.shields.io/nuget/v/Pubwiz?style=flat-square&logo=nuget&label=nuget&color=8a6cff" alt="NuGet version"></a>
  <a href="https://www.nuget.org/packages/Pubwiz"><img src="https://img.shields.io/nuget/dt/Pubwiz?style=flat-square&color=8a6cff" alt="NuGet downloads"></a>
  <a href="https://github.com/SaifAqqad/Pubwiz/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/SaifAqqad/Pubwiz/build.yml?branch=main&style=flat-square&label=build" alt="Build status"></a>
  <img src="https://img.shields.io/badge/.NET-10-512bd4?style=flat-square&logo=dotnet" alt=".NET 10">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Unlicense-blue?style=flat-square" alt="License: Unlicense"></a>
</p>

<p align="center">
  <img src="assets/screenshots/publishing.png" alt="Pubwiz publishing four projects in parallel, with live steps and build output">
</p>

> [!NOTE]
> Pubwiz is in early development. Bug reports are very welcome.

## Features

- Finds the solutions and projects in your folder
- Uses your existing `.pubxml` profiles, or imports `.publishsettings`
- Publishes several projects at once, with live progress
- Saves batches as presets you can commit and run in CI
- Keeps passwords in your OS keychain, or gets them from Azure for App Service profiles
- Shows your git branch, last commit and commits waiting to be pulled before you publish

## Install

Pubwiz is a [.NET tool](https://learn.microsoft.com/dotnet/core/tools/global-tools). It needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) or later.

```bash
dotnet tool install -g Pubwiz
```

Update it later with `dotnet tool update -g Pubwiz`.

## Quick start

Run it in a folder with a solution or project:

```bash
dotnet pubwiz
```

**1. Pick the projects.** Type to search, press <kbd>Space</kbd> to select several for a batch (<kbd>Ctrl</kbd>+<kbd>A</kbd> selects all), or <kbd>Enter</kbd> to publish just the highlighted one.

<p align="center"><img src="assets/screenshots/select-projects.png" alt="Selecting three of the solution's projects"></p>

**2. Pick a profile for each one**, or create one. New profiles start from a target, grouped by where the app is going:

<p align="center"><img src="assets/screenshots/choose-target.png" alt="Choosing a publish target, grouped into Local, Servers, Azure and Containers" width="820"></p>

Every list can be searched by typing. <kbd>Esc</kbd> or <kbd>Ctrl</kbd>+<kbd>C</kbd> in the later steps goes back to the project list.

**3. Watch it publish**, then export the logs or a report if you need them:

<p align="center"><img src="assets/screenshots/results.png" alt="The results table after publishing, with export options" width="600"></p>

## Targets

- **Local:** folder, zip file
- **Servers:** IIS (Web Deploy, Windows only), FTP/FTPS, SFTP
- **Azure:** App Service, Function Apps, WebJobs, Container Apps
- **Containers:** Docker/Podman, tarball, any registry
- **Mobile:** Android (`.aab`, `.apk`), iOS (`.ipa`, macOS only)

Azure targets sign in with the [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) or use a `.publishsettings` file. Dockerfile builds need Docker or Podman.

## Commands

| Command | Description |
|---|---|
| `dotnet pubwiz [path]` | Pick projects and profiles, then publish. `path` can be a folder, solution or project. |
| `dotnet pubwiz --preset <name>` | Publish a saved preset without the wizard. |
| `dotnet pubwiz --project <name> --profile <name>` | Publish specific projects without the wizard (`--project` is repeatable). |
| `dotnet pubwiz transform [path]` | Write the transformed `web.config` a publish would produce, without publishing. |
| `dotnet pubwiz presets list` | Show the presets in `pubwiz.json`. |
| `dotnet pubwiz presets export <name>` | Write one preset to its own file, to run with `--preset-file`. |

<details>
<summary><b>All publish options</b></summary>

| Option | Description |
|---|---|
| `-c`, `--configuration` | Build configuration, overriding the profiles' (e.g. `Release` or `Debug`). |
| `--preset <NAME>` | Run a saved preset from `pubwiz.json`. |
| `--preset-file <FILE>` | The preset file to use (default: `pubwiz.json` in the folder or its parents). |
| `--project <NAME>` | Publish this project (name or `.csproj` path) without the wizard; repeatable. Needs `--profile`. |
| `--profile <NAME>` | The publish profile used with `--project`. |
| `-y`, `--yes` | Don't ask for confirmation, and skip the menu after publishing. |
| `--all` | Also list projects that normally aren't published (libraries, tests, Aspire AppHosts). |
| `--parallel <N>` | How many projects to publish at the same time (default 1, or the preset's). |
| `--show-log` | Show live build output while publishing (toggle with <kbd>L</kbd>). |
| `--log-lines <N>` | How many lines of live output to show per project (default 10). |
| `--report <FILE>` | Write a report after publishing (`.json` or `.csv`). |
| `--log-dir <FOLDER>` | Write each project's full log to this folder. |
| `--verbose` | Use detailed build output. |

</details>

## Presets

Presets save a batch of projects and profiles, so the next publish is one command. Save one from the wizard: choose **Save a profile as a preset** in a project's profile list, or **Save this selection as a preset** before publishing several projects. They're stored in `pubwiz.json` next to your solution:

```json
{
  "presets": {
    "release": {
      "configuration": "Release",
      "parallel": 4,
      "projects": [
        { "path": "src/Shop.Api/Shop.Api.csproj", "profile": "Production" },
        { "path": "src/Shop.Worker/Shop.Worker.csproj", "profile": "Production-WebJob" }
      ]
    }
  }
}
```

```bash
dotnet pubwiz --preset release
```

When a `pubwiz.json` exists, the wizard also offers its presets before asking for projects.

## CI and automation

Without a terminal (or with `CI=true`), Pubwiz never prompts. Tell it what to publish with a preset or with `--project` and `--profile`:

```bash
dotnet pubwiz --preset release --yes --report artifacts/publish.json --log-dir artifacts/logs
dotnet pubwiz --project Shop.Api --project Shop.Worker --profile Staging --yes
```

- **Passwords** are read from `PUBWIZ_PASSWORD_<PROFILE>` (the profile name in upper case, other characters as `_`), then `PUBWIZ_PASSWORD`, then Azure (for App Service profiles, with `az`), then the OS keychain.
- **Azure** profiles sign in with `az`; in CI, use `az login --service-principal` or a workload identity.
- **Exit codes:** `0` success, `1` a publish failed, `2` a required tool is missing, `3` invalid input, `130` cancelled.

## Passwords and secrets

Pubwiz never writes passwords into profiles. When a target needs one, it asks once and can save it in your OS keychain: `libsecret` on Linux, the Keychain on macOS, and Credential Manager on Windows.

For Azure App Service profiles that use publishing credentials (Zip Deploy, or Web Deploy to an App Service, e.g. from Visual Studio or a `.publishsettings` file), Pubwiz gets the password from Azure with the signed-in `az` instead of asking.

## web.config transforms

Publishing runs `dotnet publish`, so the .NET SDK applies your `web.config` transforms as usual: `web.<Configuration>.config`, `web.<Profile>.config`, `web.<Environment>.config`, then the profile's `.transform` file.

To preview the result without publishing, Run:
```bash
dotnet pubwiz transform --configuration Release --print
```

## Contributing

Issues and pull requests are welcome. To build and test:

```bash
dotnet build Pubwiz.slnx
dotnet test --solution Pubwiz.slnx
```

| Project | Contents |
|---|---|
| [`src/Pubwiz.Tool`](src/Pubwiz.Tool) | The `dotnet pubwiz` command line app: the wizard, prompts and live dashboard ([Spectre.Console](https://spectreconsole.net)) |
| [`src/Pubwiz.Core`](src/Pubwiz.Core) | The publishing logic, organized along a publish ([layout guide](src/Pubwiz.Core/README.md)) |
| [`src/Pubwiz.WebDeploy`](src/Pubwiz.WebDeploy) | A cross-platform Web Deploy client, published as its own package (in progress) |
| [`tests`](tests) | Unit and integration tests (xUnit v3) |

The [Core layout guide](src/Pubwiz.Core/README.md) walks through how a publish flows through the code and how to add a new target.

## License

Pubwiz is free and unencumbered software released into the public domain. See [LICENSE](LICENSE).
