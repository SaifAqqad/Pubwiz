using Pubwiz.Core.Discovery;
using Pubwiz.Core.Profiles;
using Pubwiz.Tool.Credentials;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Imports a .publishsettings file (Azure portal / IIS Web Deploy) or an existing .pubxml into a project.
/// Passwords found in the file go to the OS secret store, never into the .pubxml.
/// </summary>
public sealed class ImportProfilePage(IAnsiConsole console, PasswordProvider passwords)
{
    /// <returns>The profiles that were saved (possibly none).</returns>
    public async Task<IReadOnlyList<PublishProfile>> ImportAsync(ProjectInfo project, CancellationToken cancellationToken)
    {
        var file = CommonPrompts.AskForFile(console, "Profile file", PublishSettingsImporter.FileExtension, PubxmlSerializer.FileExtension);

        var imported = Path.GetExtension(file).Equals(PubxmlSerializer.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? [new ImportedProfile(PubxmlSerializer.Read(file), Password: null)]
            : ReadPublishSettings(file);

        var saved = new List<PublishProfile>();
        foreach (var (profile, password) in imported.Where(p => ConfirmOverwrite(project, p.Profile)))
        {
            var savedProfile = PublishProfileStore.Save(project.Directory, profile, overwrite: true);
            saved.Add(savedProfile);

            var passwordNote = password is not null && await passwords.TrySaveAsync(project, savedProfile, password, cancellationToken)
                ? $" [dim](password saved in {passwords.StoreName})[/]"
                : string.Empty;

            console.MarkupLine($"Imported [{Widgets.PrimaryColorMarkup}]{savedProfile.Name.EscapeMarkup()}[/]{passwordNote}");
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

        var chosen = entries.Count == 1 ? entries : ChooseEntries(entries);
        return chosen.Select(PublishSettingsImporter.ToProfile).ToList();
    }

    /// <summary>Space-selected entries are imported; Enter without selecting imports only the highlighted one.</summary>
    private IReadOnlyList<PublishSettingsEntry> ChooseEntries(IReadOnlyList<PublishSettingsEntry> entries)
    {
        var prompt = new MultiSelectPrompt<PublishSettingsEntry>("Which profiles do you want to import?", entries, e => [e.ProfileName, e.Method.ToString()])
        {
            Hint = "Select several profiles to import them all, or press enter to import only the highlighted one."
        };

        var result = console.Prompt(prompt)!;
        return result.Selected.Count > 0 ? result.Selected : [result.Highlighted];
    }

    private bool ConfirmOverwrite(ProjectInfo project, PublishProfile profile)
    {
        return !PublishProfileStore.Exists(project.Directory, profile.Name)
               || console.AskYesNo($"A profile named '{profile.Name}' already exists. Replace it?", defaultValue: false);
    }
}
