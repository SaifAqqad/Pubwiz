# Pubwiz

An interactive publish wizard for .NET: `dotnet publish`, but you pick the projects and targets from a menu.

> **Status:** early development. See [docs/PLAN.md](docs/PLAN.md) for the roadmap.

Planned targets: folder, IIS Web Deploy (cross-platform), Zip/One deploy, FTP/FTPS/SFTP, Azure App Service, WebJobs and Functions, container images (SDK or Dockerfile → local daemon, ACR or any registry, tarball), and .NET MAUI (Android, iOS).

## Commands

```bash
dotnet pubwiz [path]              # pick projects and profiles, then publish (--parallel, --show-log, -c)
dotnet pubwiz transform [path]    # write a transformed web.config without publishing
```

## Install

```bash
dotnet tool install -g Pubwiz
dotnet pubwiz
```

## Repository layout

| Path | Contents |
|---|---|
| `src/Pubwiz.Core` | Publishing logic: project discovery, profiles, CLI wrappers (dotnet, az, docker), deploy clients |
| `src/Pubwiz.Tool` | The `dotnet pubwiz` command line app (Spectre.Console UI) |
| `src/Pubwiz.WebDeploy` | Standalone cross-platform Web Deploy client, published as its own NuGet package |
| `tests/*` | Unit and integration tests (xUnit v3) |
| `tools/test-vm` | A local Windows + IIS + Web Deploy VM for testing |
