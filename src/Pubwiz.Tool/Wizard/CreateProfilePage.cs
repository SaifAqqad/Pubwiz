using Pubwiz.Core.Discovery;
using Pubwiz.Core.Infrastructure;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets;
using Pubwiz.Core.Targets.Maui;
using Pubwiz.Core.Targets.WebDeploy;
using Pubwiz.Tool.Credentials;
using Pubwiz.Tool.Rendering;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Creates a new profile: choose a target, fill in the target's settings, then name and save it.
/// </summary>
public sealed class CreateProfilePage(
    IAnsiConsole console,
    FolderProfilePage folderPage,
    ZipFileProfilePage zipFilePage,
    WebDeployProfilePage webDeployPage,
    WebDeployAlternativesPrompt webDeployAlternatives,
    FileTransferProfilePage fileTransferPage,
    AzureProfilePage azurePage,
    AwsProfilePage awsPage,
    ContainerProfilePage containerPage,
    MauiProfilePage mauiPage,
    PasswordProvider passwords)
{
    /// <summary>The options in the target menu.</summary>
    private abstract record Choice
    {
        public sealed record Target(PublishTarget Value) : Choice;

        /// <summary>A category heading (Local, Servers, Azure, ...); not selectable.</summary>
        public sealed record Group(PublishTargetGroup Value) : Choice;

        public sealed record Back : Choice;
    }

    /// <returns>The saved profile, or <c>null</c> when the user went back.</returns>
    public async Task<PublishProfile?> CreateAsync(ProjectInfo project, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (ChooseTarget(project) is not Choice.Target { Value: var target })
            {
                return null;
            }

            if (!target.IsAvailable)
            {
                console.MarkupLine($"[yellow]{target.DisplayName.EscapeMarkup()} is not available yet ({target.UnavailableReason}).[/]");
                continue;
            }

            console.Section(target.DisplayName);
            if (await TryCreateAsync(() => CreateForTargetAsync(project, target, cancellationToken)) is not { } created)
            {
                // Something the target needs is missing (or the user chose another target): back to the targets.
                continue;
            }

            // Each page names its profile (FolderProfile, "contoso - Zip Deploy", ...); that's the suggested name.
            return await SaveAsync(project, created, defaultName: created.Profile.Name, cancellationToken);
        }
    }

    /// <summary>
    /// For a Web Deploy profile that can't publish from this machine: suggests other targets for the same server
    /// and creates a profile for the chosen one (FTP/SFTP start with the server and user name filled in).
    /// </summary>
    /// <returns>The profile to publish with (the original or a new one), or <c>null</c> to go back.</returns>
    public async Task<PublishProfile?> OfferWebDeployAlternativeAsync(ProjectInfo project, PublishProfile profile, CancellationToken cancellationToken)
    {
        var endpoint = WebDeployAlternatives.TryGetEndpoint(profile);
        var reason = WebDeployProfile.BuiltInClientBlocker(profile) ?? MsDeployLocator.MissingReason;
        var keepLabel = $"Use '{profile.Name.EscapeMarkup()}' anyway [dim]· it will fail on this machine[/]";

        switch (webDeployAlternatives.Ask(endpoint, reason, keepLabel))
        {
            case WebDeployAlternativesPrompt.Choice.KeepWebDeploy:
                return profile;

            case WebDeployAlternativesPrompt.Choice.Alternative { Type: var type }:
                console.Section(FindTarget(project, type).DisplayName);
                var defaults = new FileTransferDefaults(endpoint?.ServiceUrl.Host, profile[ProfileProperties.UserName], profile[ProfileProperties.SiteUrlToLaunchAfterPublish]);
                var created = await TryCreateAsync(async () => type switch
                {
                    PublishTargetType.Ftp => new CreatedProfile(fileTransferPage.Create(project, sftp: false, defaults)),
                    PublishTargetType.Sftp => new CreatedProfile(fileTransferPage.Create(project, sftp: true, defaults)),
                    _ => await CreateForTargetAsync(project, FindTarget(project, type), cancellationToken)
                });

                if (created is null)
                {
                    return null;
                }

                return await SaveAsync(project, created, defaultName: $"{profile.Name}-{type}", cancellationToken);

            default:
                return null;
        }
    }

    /// <summary>
    /// Runs a target's settings page. When it can't go on (no docker, az missing, no matching apps, ...), the error is
    /// shown and <c>null</c> is returned, so the wizard goes back instead of ending Pubwiz.
    /// </summary>
    private async Task<CreatedProfile?> TryCreateAsync(Func<Task<CreatedProfile>> create)
    {
        try
        {
            return await create();
        }
        catch (BackToTargetsException)
        {
            console.WriteLine();
            return null;
        }
        catch (Exception ex) when (ex is UserErrorException or PrerequisiteMissingException)
        {
            console.Write(Widgets.Error(ex.Message));
            console.WriteLine();
            console.WriteLine();
            return null;
        }
    }

    private static PublishTarget FindTarget(ProjectInfo project, PublishTargetType type) =>
        PublishTargetCatalog.For(project.Kind).Single(target => target.Type == type);

    /// <summary>Asks for a name, saves the profile, and offers to keep its password in the keychain.</summary>
    private async Task<PublishProfile> SaveAsync(ProjectInfo project, CreatedProfile created, string defaultName, CancellationToken cancellationToken)
    {
        var saved = Save(project, created.Profile, defaultName);
        if (created.Password is not null)
        {
            await passwords.TrySaveAsync(project, saved, created.Password, cancellationToken);
        }

        return saved;
    }

    /// <summary>Runs the settings page of the chosen target.</summary>
    private async Task<CreatedProfile> CreateForTargetAsync(ProjectInfo project, PublishTarget target, CancellationToken cancellationToken) => target.Type switch
    {
        PublishTargetType.Folder => new CreatedProfile(folderPage.Create(project)),
        PublishTargetType.ZipFile => new CreatedProfile(zipFilePage.Create(project)),
        PublishTargetType.WebDeploy => new CreatedProfile(webDeployPage.Create(project)),
        PublishTargetType.Ftp => new CreatedProfile(fileTransferPage.Create(project, sftp: false)),
        PublishTargetType.Sftp => new CreatedProfile(fileTransferPage.Create(project, sftp: true)),
        PublishTargetType.AzureAppService => new CreatedProfile(await azurePage.CreateAsync(project, AzureAppKind.WebApp, cancellationToken)),
        PublishTargetType.AzureFunctions => new CreatedProfile(await azurePage.CreateAsync(project, AzureAppKind.FunctionApp, cancellationToken)),
        PublishTargetType.AzureWebJob => new CreatedProfile(await azurePage.CreateAsync(project, AzureAppKind.WebJob, cancellationToken)),
        PublishTargetType.AwsElasticBeanstalk => new CreatedProfile(await awsPage.CreateElasticBeanstalkAsync(project, cancellationToken)),
        PublishTargetType.AwsLambda => new CreatedProfile(await awsPage.CreateLambdaAsync(project, cancellationToken)),
        PublishTargetType.LocalContainer or PublishTargetType.ContainerTarball or PublishTargetType.ContainerRegistry
            or PublishTargetType.AzureAppServiceContainer or PublishTargetType.AzureContainerApps or PublishTargetType.AwsEcs
            => new CreatedProfile(await containerPage.CreateAsync(project, target.Type, cancellationToken)),
        PublishTargetType.MauiAndroid => await mauiPage.CreateAsync(project, MauiPlatform.Android, cancellationToken),
        PublishTargetType.MauiIos => await mauiPage.CreateAsync(project, MauiPlatform.Ios, cancellationToken),
        _ => throw new NotSupportedException($"{target.DisplayName} is not implemented.")
    };

    /// <summary>
    /// One list with the targets under category headings. The cursor skips the headings (leaf mode),
    /// so every target is one keypress away instead of behind a category page.
    /// </summary>
    private Choice ChooseTarget(ProjectInfo project)
    {
        var prompt = Questions.Menu<Choice>($"Where do you want to publish [{Widgets.PrimaryColorMarkup}]{project.Name.EscapeMarkup()}[/]?")
            .Mode(SelectionMode.Leaf)
            .PageSize(24)
            .AddCancelResult(new Choice.Back())
            .UseConverter(Describe);

        foreach (var group in PublishTargetCatalog.For(project.Kind).GroupBy(t => t.Group).OrderBy(g => g.Key))
        {
            prompt.AddChoiceGroup(new Choice.Group(group.Key), group.Select(target => new Choice.Target(target)));
        }

        prompt.AddChoice(new Choice.Back());
        return console.Prompt(prompt);
    }

    private static string Describe(Choice choice)
    {
        switch (choice)
        {
            case Choice.Group { Value: var group }:
                return $"[bold]{group}[/]";

            case Choice.Target { Value: { IsAvailable: false } target }:
                return $"[grey]{target.DisplayName.EscapeMarkup()} ({target.UnavailableReason})[/]";

            case Choice.Target { Value: var target }:
                var warning = target.Warning is null ? string.Empty : $" [yellow]({target.Warning.EscapeMarkup()})[/]";
                return $"{target.DisplayName.EscapeMarkup()} [dim]· {target.Description.EscapeMarkup()}[/]{warning}";

            default:
                return "[dim]← Back[/]";
        }
    }

    private PublishProfile Save(ProjectInfo project, PublishProfile profile, string defaultName)
    {
        console.WriteLine();
        var name = console.Ask("Profile name", defaultValue: NextFreeName(project, defaultName),
            validate: value => PublishProfileStore.Exists(project.Directory, value) ? "A profile with that name already exists" : null);

        var saved = PublishProfileStore.Save(project.Directory, profile.CopyAs(PublishProfileStore.ToValidName(name), filePath: null));
        console.MarkupLine($"{Widgets.SuccessCheckMarkup} Saved [{Widgets.PrimaryColorMarkup}]{DisplayText.Link(saved.FilePath!, Path.GetRelativePath(project.Directory, saved.FilePath!))}[/]");
        return saved;
    }

    /// <summary>
    /// Visual Studio style default names: FolderProfile, FolderProfile1, FolderProfile2, ...
    /// </summary>
    private static string NextFreeName(ProjectInfo project, string baseName)
    {
        var name = baseName;
        for (var i = 1; PublishProfileStore.Exists(project.Directory, name); i++)
        {
            name = $"{baseName}{i}";
        }

        return name;
    }
}
