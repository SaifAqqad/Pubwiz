# Pubwiz.Core

The publishing logic behind `dotnet pubwiz`, with no UI (the wizard and terminal output live in `Pubwiz.Tool`).
The folders follow a publish from start to finish.

## How a publish flows

```
 Discovery  ──►  Profiles  ──►  Publishing  ──►  Targets/<destination>  ──►  Reporting
 find the        how each       run each job's   the steps that put the      export logs
 projects        one is         steps in order   app somewhere               and reports
                 published
```

1. **`Discovery/`** finds what can be published. `WorkspaceScanner` looks for solutions and projects, `SolutionReader`
   reads `.sln`/`.slnx` files, and `ProjectInspector` asks MSBuild about each project (target frameworks, and its
   `ProjectKind` via `ProjectKindDetector`). `WorkspaceLoader` puts it together as a `Workspace`.
2. **`Profiles/`** is about publish profiles. A `PublishProfile` is a `.pubxml` file in memory; `PublishProfileStore`
   lists, reads and saves them in `Properties/PublishProfiles/`, and `PublishSettingsImporter` turns a
   `.publishsettings` file into profiles. `ProfileProperties` names every property Pubwiz reads or writes.
3. **`Publishing/`** is the engine. `PublishRunner.CreateJob` takes a `PublishRequest` (project + profile), finds the
   first `IPublishTarget` that can publish the profile, and asks it for the job's `PublishStep`s. `RunAsync` then runs
   the steps of each job in order (several jobs can run in parallel). Steps share a `PublishContext` and write to the
   job's `PublishLog`. `Steps/` holds the steps several targets use, like the staging `dotnet publish`.
4. **`Targets/`** has one folder per destination. Each holds the target (`IPublishTarget`), its own steps, and the
   clients it needs, so everything about a destination is in one place:

   | Folder | Publishes to | Also contains |
   |---|---|---|
   | `Folder/` | a folder, or a zip file | |
   | `FileTransfer/` | FTP/FTPS and SFTP servers | the FTP and SFTP uploaders, SSH commands |
   | `WebDeploy/` | IIS, with `msdeploy.exe` (Windows) | the msdeploy wrapper, alternatives for other OSes |
   | `Azure/` | App Service, Function Apps, WebJobs, Static Web Apps | the `az` wrapper, `Kudu/` (deploy API client), `StaticWebApps/` (StaticSitesClient) |
   | `Aws/` | Elastic Beanstalk, ECS, Lambda | the `aws` wrapper, Beanstalk bundles, task definition revisions |
   | `Containers/` | local images, tarballs, registries, Azure and ECS container hosts | the docker/podman wrapper |
   | `Maui/` | Android and iOS packages | keystore and Apple signing helpers |

   `PublishTargetCatalog` lists the targets the wizard offers for each kind of project.
5. **`Reporting/`** exports what happened: each job's full log, and a JSON or CSV report.

Other folders:

- **`Presets/`** saves and loads batches of projects and profiles (`pubwiz.json`).
- **`Transforms/`** applies `web.config` transforms the way `dotnet publish` does (also used by `pubwiz transform`).
- **`Dotnet/`** wraps the `dotnet` CLI: MSBuild evaluation for discovery, and `dotnet publish` for the targets.
- **`Infrastructure/`** is plumbing with no publishing knowledge: running processes (`Processes/`), OS and `PATH`
  helpers (`Platform/`), and the OS keychains (`Secrets/`).

## Adding a target

1. Add a `PublishTargetType` value and an entry in `PublishTargetCatalog` (name, group, and the project kinds it's for).
2. Create `Targets/<Name>/<Name>Target.cs` implementing `IPublishTarget`:
   - `CanPublish`: which profiles it handles (usually by `WebPublishMethod`).
   - `RequiresPassword`, and `GetSetupError` if it needs something this machine may not have.
   - `CreateSteps`: usually `StagingPublish.CreateStep` first, then the target's own steps, then `SetDestinationStep`.
3. In `Pubwiz.Tool`, register the target in `Infrastructure/ServiceRegistration.cs` (the first target that can publish a
   profile wins, so order matters) and add a profile page under `Wizard/`.
4. Add tests under `tests/Pubwiz.Core.Tests/Targets/<Name>/`; the test project mirrors these folders.
