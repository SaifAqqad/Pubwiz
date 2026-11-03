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

It uses the `.pubxml` profiles already in your repository (or imports a `.publishsettings` file), and it also:

- publishes several projects at once, with each one's build output on screen as it runs
- deploys to IIS with Web Deploy from Windows, macOS or Linux
- saves a batch of projects as a preset in `pubwiz.json`, so the next release is one command, on your machine or in CI
- keeps passwords out of your repository: in your OS keychain, or fetched from Azure for App Service

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
dotnet pubwiz servers set <server>                 # save a Web Deploy login for a server
```

Run `dotnet pubwiz --help` for every option. Full documentation, screenshots and the source are on [GitHub](https://github.com/SaifAqqad/Pubwiz).
