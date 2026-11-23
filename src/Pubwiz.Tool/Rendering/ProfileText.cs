using Pubwiz.Core.Profiles;
using Pubwiz.Core.Targets.Azure;

namespace Pubwiz.Tool.Rendering;

/// <summary>
/// How a profile is written in lists and answers: its name, then its details in dim parentheses,
/// e.g. <c>contoso - Zip Deploy (ZipDeploy · staging slot)</c>.
/// </summary>
internal static class ProfileText
{
    /// <summary>The name in <paramref name="nameStyle"/>, then the details dimmed.</summary>
    /// <param name="profile">The profile.</param>
    /// <param name="nameStyle">Markup style of the name, e.g. the primary color; <c>null</c> for plain.</param>
    public static string ToMarkup(PublishProfile profile, string? nameStyle = null)
    {
        var name = profile.Name.EscapeMarkup();
        var styledName = nameStyle is null ? name : $"[{nameStyle}]{name}[/]";
        return $"{styledName} [dim]({Details(profile).EscapeMarkup()})[/]";
    }

    /// <summary>
    /// The publish method, and the Azure deployment slot when the profile publishes to one. Pubwiz puts the slot in the
    /// profile's name, but Visual Studio and imported profiles may not, and publishing to the wrong slot is easy to miss.
    /// </summary>
    public static string Details(PublishProfile profile)
    {
        var slot = AzureProfile.GetDeploymentSlot(profile);
        return slot is null || profile.Name.Contains(slot, StringComparison.OrdinalIgnoreCase)
            ? profile.Method.ToString()
            : $"{profile.Method} · {slot} slot";
    }
}
