# Pubwiz

**The publish wizard for .NET.** Pick your projects and where they go. Pubwiz does the rest.

![Pubwiz publishing four projects in parallel](https://raw.githubusercontent.com/SaifAqqad/Pubwiz/main/assets/screenshots/publishing.png)

## Install

Requires the .NET 10 SDK or later.

```bash
dotnet tool install --global Pubwiz
```

## Use it

Run it in a folder with a solution or project, then pick the projects and a publish profile for each:

```bash
dotnet pubwiz
```

- Uses your existing `.pubxml` profiles, or imports `.publishsettings`
- Publishes several projects at once, with live progress
- Saves batches as presets you can commit and run in CI

## Targets

- **Local:** folder, zip file
- **Servers:** IIS (Web Deploy, Windows only), FTP/FTPS, SFTP
- **Azure:** App Service, Function Apps, WebJobs, Container Apps
- **Containers:** Docker/Podman, tarball, any registry
- **Mobile:** Android (`.aab`, `.apk`), iOS (`.ipa`, macOS only)

## Commands

```bash
dotnet pubwiz [path]                              # the wizard
dotnet pubwiz --preset <name>                     # publish a saved preset
dotnet pubwiz --project <name> --profile <name>   # publish without the wizard
dotnet pubwiz transform [path]                    # preview the transformed web.config
dotnet pubwiz presets list                        # show saved presets
```

Run `dotnet pubwiz --help` for every option. Full documentation, screenshots and the source are on [GitHub](https://github.com/SaifAqqad/Pubwiz).
