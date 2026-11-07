# Pubwiz

**The publish wizard for .NET.** Pick your projects and where they go. Pubwiz does the rest.

![Pubwiz publishing four projects in parallel](https://raw.githubusercontent.com/SaifAqqad/Pubwiz/main/assets/screenshots/publishing.png)

## Install

Requires the .NET 10 SDK or later.

```bash
dotnet tool install --global Pubwiz
```

Or run it once without installing, with `dnx Pubwiz`.

## Use it

Run it in a folder with a solution or project, then pick the projects and a publish profile for each:

```bash
dotnet pubwiz
```

It's the publish step of Visual Studio, in your terminal: it uses the `.pubxml` profiles already in your repository
(or imports a `.publishsettings` file), on any OS.

- **Web Deploy to IIS from macOS and Linux,** with its own client: no Windows or `msdeploy.exe` needed
- **Several projects in one go,** in the order you choose, with each one's build output on screen as it runs
- **Azure from the same wizard:** App Service, Function Apps, WebJobs and Container Apps, signed in with the Azure CLI, and Static Web Apps (Blazor WebAssembly) without Node or the SWA CLI
- presets in `pubwiz.json`, so a release is one command, on your machine or in CI

## Targets

- **Local:** folder, zip file
- **Servers:** IIS (Web Deploy, from Windows, Linux and macOS), FTP/FTPS, SFTP (optionally running commands over SSH before and after the upload)
- **Azure:** App Service, Function Apps, WebJobs, Container Apps, Static Web Apps (Blazor WebAssembly)
- **Containers:** Docker/Podman, tarball, any registry
- **Mobile:** Android (`.aab`, `.apk`), iOS (`.ipa`, macOS only)

## Commands

```bash
dotnet pubwiz [path]                              # the wizard
dotnet pubwiz --preset <name>                     # publish a saved preset
dotnet pubwiz --project <name> --profile <name>   # publish without the wizard
dotnet pubwiz transform [path]                    # preview the transformed web.config
dotnet pubwiz presets list                        # show saved presets
dotnet pubwiz servers set <server>                # save a login for a server
```

Run `dotnet pubwiz --help` for every option. Full documentation, screenshots and the source are on [GitHub](https://github.com/SaifAqqad/Pubwiz).
