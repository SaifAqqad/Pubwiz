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

`dotnet publish` is great at building, but shipping an app still means remembering profile names, MSBuild properties, upload tools and which projects belong together. **Pubwiz** turns that into a short conversation in your terminal, and remembers the answers.

> [!NOTE]
> Pubwiz is in early development. Folder, zip file, SFTP and local container builds are tested end to end; Azure, FTP, registry pushes and MAUI haven't been run against real services yet. Bug reports are very welcome.

## Highlights

- **Finds your projects.** Solutions (`.sln`, `.slnx`) first, then project files, up to two folders deep. Libraries and test projects are hidden unless you ask for them.
- **Uses real publish profiles.** Profiles are ordinary `Properties/PublishProfiles/*.pubxml` files that Visual Studio and `dotnet publish -p:PublishProfile=...` understand. Import a `.publishsettings` or `.pubxml`, or create one in the wizard.
- **Publishes in batches.** Select several projects and publish them one after another or in parallel, with a live checklist of every step and an optional log tail (press <kbd>L</kbd>).
- **Remembers batches as presets.** Save a selection to `pubwiz.json`, commit it, and the whole team (and CI) can run `dotnet pubwiz --preset release`.
- **Handles the details.** `app_offline.htm` while uploading, passwords in your OS keychain, Azure sign-in through `az`, and a preview of your `web.config` transforms.
- **Plays well with CI.** No prompts without a terminal, passwords from environment variables, JSON/CSV reports, full logs and meaningful exit codes.

## Install

Pubwiz is a [.NET tool](https://learn.microsoft.com/dotnet/core/tools/global-tools). It needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) or later.

```bash
dotnet tool install --global Pubwiz
```

Update it later with `dotnet tool update --global Pubwiz`.

## Quick start

Run it in a folder with a solution or project:

```bash
dotnet pubwiz
```

**1. Pick the projects.** Press <kbd>Space</kbd> to select several for a batch, or <kbd>Enter</kbd> to publish just the highlighted one.

<p align="center"><img src="assets/screenshots/select-projects.png" alt="Selecting three of the solution's projects"></p>

**2. Pick a profile for each one**, or create one. New profiles start from a target, grouped by where the app is going:

<p align="center"><img src="assets/screenshots/choose-target.png" alt="Choosing a publish target, grouped into Local, Servers, Azure and Containers" width="820"></p>

**3. Watch it publish**, then export the logs or a report if you need them:

<p align="center"><img src="assets/screenshots/results.png" alt="The results table after publishing, with export options" width="600"></p>

## Where you can publish

| Target | What Pubwiz does | Needs |
|---|---|---|
| **Folder** | `dotnet publish` with your profile, into any folder | |
| **Zip file** | Publishes, then zips the output, ready to copy to a server | |
| **IIS (Web Deploy)** | Syncs to IIS with the official `msdeploy.exe` (Basic or Windows sign-in, app offline, keep or delete extra files) | Windows with [Web Deploy](https://www.iis.net/downloads/microsoft/web-deploy) |
| **FTP / FTPS** | Uploads the output, optionally removing old files and taking the app offline while it uploads | |
| **SFTP** | The same over SSH, with a password or a private key | |
| **Azure App Service** | Zip deploy through Kudu, waiting for the deployment to finish; Windows and Linux apps | [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), or a `.publishsettings` file |
| **Azure Function App** | The same, for Function Apps | Azure CLI or `.publishsettings` |
| **Azure WebJob** | Deploys a console app or worker as a continuous or scheduled WebJob | Azure CLI or `.publishsettings` |
| **Container image** | Builds with the .NET SDK or your `Dockerfile`, then loads it into Docker/Podman, saves a tarball, or pushes to any registry | Docker or Podman for local images and Dockerfiles |
| **Azure containers** | Pushes to Azure Container Registry, then points an App Service or Container App at the new image | Azure CLI |
| **Android** | A signed `.aab` or `.apk`; can create the keystore for you | .NET MAUI workload, JDK |
| **iOS** | A signed `.ipa` with your identity and provisioning profile | macOS, Xcode, .NET MAUI workload |

On Linux and macOS, where `msdeploy.exe` doesn't run, picking IIS (Web Deploy) suggests FTP, SFTP or a zip file for the same server. Web Deploy profiles from Azure (`.publishsettings`) are published through Kudu on every OS.

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

- **Passwords** are read from `PUBWIZ_PASSWORD_<PROFILE>` (the profile name in upper case, other characters as `_`), then `PUBWIZ_PASSWORD`, then the OS keychain.
- **Azure** profiles sign in with `az`; in CI, use `az login --service-principal` or a workload identity.
- **Exit codes:** `0` success, `1` a publish failed, `2` a required tool is missing, `3` invalid input, `130` cancelled.

## Passwords and secrets

Pubwiz never writes passwords into profiles. When a target needs one, it asks once and can save it in your OS keychain: the Secret Service (libsecret) on Linux, the Keychain on macOS, and Credential Manager on Windows. Passwords are masked in the live log and in exported logs.

## web.config transforms

Publishing runs `dotnet publish`, so the .NET SDK applies your `web.config` transforms as usual: `web.<Configuration>.config`, `web.<Profile>.config`, `web.<Environment>.config`, then the profile's `.transform` file. To preview the result, in the same order, without publishing:

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
