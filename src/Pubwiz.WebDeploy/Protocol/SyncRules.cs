using System.Security;
using System.Text;

namespace Pubwiz.WebDeploy.Protocol;

/// <summary>
/// Builds the <c>&lt;rules&gt;</c> XML of the <c>MSDeploy.SyncOptions</c> header: msdeploy's sync rules, each on or off,
/// plus the skip rules that only prevent one action (<c>skipAction</c>).
/// </summary>
/// <remarks>
/// msdeploy keeps its rules in one fixed order and writes the enabled ones first, then the disabled ones, each group in
/// that order (e.g. <c>DoNotDeleteRule</c> moves up among the enabled rules when it's turned on). Skip-action rules come
/// before both groups. Checked byte for byte against captures; see docs/protocol.md.
/// </remarks>
internal static class SyncRules
{
    public const string AppOffline = "AppOffline";
    public const string DoNotDelete = "DoNotDeleteRule";

    /// <summary>Every rule in msdeploy's order, and whether it's on by default.</summary>
    private static readonly (string Name, bool EnabledByDefault)[] Defaults =
    [
        ("EnvironmentVariableNormalize", true),
        ("Parameterization", true),
        ("AboFilter", true),
        ("AnonymousUser", true),
        ("AppPoolIdentity", true),
        ("AppRootNormalize", true),
        ("BlockHarmfulDeleteOperations", true),
        ("BlockUnsupportedDeleteOperations", true),
        ("ClassicAppPoolProtectRule", true),
        ("CreateApplicationRule", true),
        ("CrossPlatformRule", true),
        (DoNotDelete, false),
        ("EncryptWebConfig", false),
        ("IgnoreConfigInheritanceRule", false),
        ("IISConfigFrom64To32", true),
        ("IISSchemaDifferenceRule", false),
        ("MetakeyToIIS6", true),
        ("SchemaSection", true),
        ("SkipInvalidSource", true),
        ("SkipNewerFilesRule", false),
        ("SkipUNC", true),
        ("SyncGeneral", true),
        ("SyncXP", true),
        ("UrlScanSkipIncompat", true),
        ("WarnForEncryptedDataRule", false),
        ("XpIsapis", true),
        ("IgnoreFileLastWriteTime", true),
        (AppOffline, false),
        ("DependencyCheckAppPoolExists", true),
        ("DependencyCheckInUse", true),
        ("DependencyCheckFailOnError", true),
        ("DependencyCheckFailOnWarning", false)
    ];

    /// <summary>The rules XML for a sync with these options.</summary>
    public static string ToXml(WebDeploySyncOptions options)
    {
        var enabled = Defaults.ToDictionary(r => r.Name, r => r.EnabledByDefault);
        enabled[AppOffline] = options.TakeAppOffline;
        enabled[DoNotDelete] = !options.DeleteExtraFiles;

        var xml = new StringBuilder("<rules>");

        var actionRules = options.SkipRules.Where(r => r.Action is not null).ToList();
        for (var i = 0; i < actionRules.Count; i++)
        {
            var rule = actionRules[i];
            xml.Append($"<skip skipAction=\"{rule.Action}\" objectName=\"{rule.ObjectName}\" absolutePath=\"{SecurityElement.Escape(rule.AbsolutePath)}\" ")
               .Append($"xPath=\"\" name=\"CommandLineSkipActionRule{i + 1}\" />");
        }

        foreach (var (name, _) in Defaults.Where(r => enabled[r.Name]))
        {
            xml.Append($"<enable name=\"{name}\" />");
        }

        foreach (var (name, _) in Defaults.Where(r => !enabled[r.Name]))
        {
            xml.Append($"<disable name=\"{name}\" />");
        }

        return xml.Append("</rules>").ToString();
    }
}
