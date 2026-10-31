using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Creates a new profile: choose a target, fill in the target's settings, then name and save it.
/// </summary>
public sealed class CreateProfilePage(IAnsiConsole console, FolderProfilePage folderPage, FileTransferProfilePage fileTransferPage)
{
    /// <summary>The options in the target menu.</summary>
    private abstract record Choice
    {
        public sealed record Target(PublishTarget Value) : Choice;

        public sealed record Back : Choice;
    }

    /// <returns>The saved profile, or <c>null</c> when the user went back.</returns>
    public PublishProfile? Create(ProjectInfo project)
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

            var profile = target.Type switch
            {
                PublishTargetType.Folder => folderPage.Create(project),
                PublishTargetType.Ftp => fileTransferPage.Create(project, sftp: false),
                PublishTargetType.Sftp => fileTransferPage.Create(project, sftp: true),
                _ => throw new NotSupportedException($"{target.DisplayName} is not implemented.")
            };

            return Save(project, profile, defaultName: $"{target.Type}Profile");
        }
    }

    private Choice ChooseTarget(ProjectInfo project)
    {
        var targets = PublishTargetCatalog.For(project.Kind)
            .OrderBy(t => t.Group)
            .Select(t => new Choice.Target(t));

        return console.Prompt(new SelectionPrompt<Choice>()
            .Title($"Where do you want to publish [{Widgets.PrimaryColorMarkup}]{project.Name.EscapeMarkup()}[/]?")
            .HighlightStyle(new Style(Widgets.PrimaryColor))
            .PageSize(16)
            .AddChoices(targets)
            .AddChoices(new Choice.Back())
            .UseConverter(Describe));
    }

    private static string Describe(Choice choice)
    {
        if (choice is not Choice.Target { Value: var target })
        {
            return "[dim]← Back[/]";
        }

        return target.IsAvailable
            ? $"{target.DisplayName.EscapeMarkup()} [dim]· {target.Group} · {target.Description.EscapeMarkup()}[/]"
            : $"[grey]{target.DisplayName.EscapeMarkup()} · {target.Group} ({target.UnavailableReason})[/]";
    }

    private PublishProfile Save(ProjectInfo project, PublishProfile profile, string defaultName)
    {
        var name = console.Prompt(new TextPrompt<string>("Profile name:")
            .DefaultValue(NextFreeName(project, defaultName))
            .Validate(value => PublishProfileStore.Exists(project.Directory, value)
                ? ValidationResult.Error("[red]A profile with that name already exists[/]")
                : ValidationResult.Success()));

        var saved = PublishProfileStore.Save(project.Directory, profile.CopyAs(PublishProfileStore.ToValidName(name), filePath: null));
        console.MarkupLine($"Saved [{Widgets.PrimaryColorMarkup}]{Path.GetRelativePath(project.Directory, saved.FilePath!).EscapeMarkup()}[/]");
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
