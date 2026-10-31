using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Imports a .publishsettings file (Azure portal / IIS Web Deploy) or an existing .pubxml into a project.
/// </summary>
public sealed class ImportProfilePage(IAnsiConsole console)
{
    /// <returns>The profiles that were saved (possibly none).</returns>
    public IReadOnlyList<PublishProfile> Import(ProjectInfo project)
    {
        var file = CommonPrompts.AskForFile(console, "Path to the profile file:", PublishSettingsImporter.FileExtension, PubxmlSerializer.FileExtension);

        var imported = Path.GetExtension(file).Equals(PubxmlSerializer.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? [new ImportedProfile(PubxmlSerializer.Read(file), Password: null)]
            : ReadPublishSettings(file);

        var saved = imported
            .Where(p => ConfirmOverwrite(project, p.Profile))
            .Select(p => PublishProfileStore.Save(project.Directory, p.Profile, overwrite: true))
            .ToList();

        foreach (var profile in saved)
        {
            console.MarkupLine($"Imported [{Widgets.PrimaryColorMarkup}]{profile.Name.EscapeMarkup()}[/]");
        }

        if (imported.Any(p => p.Password is not null))
        {
            // Passwords are kept in the OS keychain, which arrives together with publishing.
            console.MarkupLine("[dim]Passwords in the file were not saved; you'll be asked for them when publishing.[/]");
        }

        return saved;
    }

    private List<ImportedProfile> ReadPublishSettings(string file)
    {
        var entries = PublishSettingsImporter.Read(file);
        if (entries.Count == 0)
        {
            throw new UserErrorException($"'{Path.GetFileName(file)}' contains no Web Deploy, FTP or Zip Deploy profiles.");
        }

        var chosen = entries.Count == 1
            ? entries
            : console.Prompt(new MultiSelectionPrompt<PublishSettingsEntry>()
                .Title("Which profiles do you want to import?")
                .HighlightStyle(new Style(Widgets.PrimaryColor))
                .InstructionsText("[dim](space to select, enter to import)[/]")
                .AddChoices(entries)
                .UseConverter(e => $"{e.ProfileName.EscapeMarkup()} [dim]({e.Method})[/]"));

        return chosen.Select(PublishSettingsImporter.ToProfile).ToList();
    }

    private bool ConfirmOverwrite(ProjectInfo project, PublishProfile profile)
    {
        return !PublishProfileStore.Exists(project.Directory, profile.Name)
               || console.Confirm($"A profile named '{profile.Name.EscapeMarkup()}' already exists. Replace it?", defaultValue: false);
    }
}
