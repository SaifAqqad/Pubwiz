using Pubwiz.Core.Platform;

namespace Pubwiz.Core.Azure;

/// <summary>
/// A way to install the Azure CLI on this machine.
/// </summary>
/// <param name="Description">e.g. "Install with dnf (Fedora/RHEL)".</param>
/// <param name="Command">The shell command, shown to the user and run only after they confirm.</param>
public sealed record AzCliInstallOption(string Description, string Command);

/// <summary>
/// Suggests how to install the Azure CLI, based on the OS and Linux distribution.
/// </summary>
/// <seealso href="https://learn.microsoft.com/en-us/cli/azure/install-azure-cli"/>
public static class AzCliInstallGuide
{
    public const string DocsUrl = "https://learn.microsoft.com/cli/azure/install-azure-cli";

    /// <summary>The recommended install option, or <c>null</c> when only the docs can help.</summary>
    public static AzCliInstallOption? GetRecommended()
    {
        if (OperatingSystemInfo.IsWindows)
        {
            return new AzCliInstallOption("Install with winget", "winget install --exact --id Microsoft.AzureCLI");
        }

        if (OperatingSystemInfo.IsMacOS)
        {
            return new AzCliInstallOption("Install with Homebrew", "brew update && brew install azure-cli");
        }

        const string osReleasePath = "/etc/os-release";
        var osRelease = File.Exists(osReleasePath) ? File.ReadAllText(osReleasePath) : string.Empty;
        return ForLinux(osRelease, isImageBased: File.Exists("/run/ostree-booted"), hasHomebrew: ExecutableLocator.FindInPath("brew") is not null);
    }

    /// <param name="osRelease">The contents of <c>/etc/os-release</c>.</param>
    /// <param name="isImageBased">Image-based (ostree) systems like Fedora Silverblue or Bazzite can't install packages with dnf.</param>
    /// <param name="hasHomebrew">Whether Homebrew is installed.</param>
    internal static AzCliInstallOption? ForLinux(string osRelease, bool isImageBased, bool hasHomebrew)
    {
        if (isImageBased)
        {
            return hasHomebrew ? new AzCliInstallOption("Install with Homebrew (image-based system)", "brew install azure-cli") : null;
        }

        var ids = ReadDistributionIds(osRelease);
        if (ids.Overlaps(["debian", "ubuntu"]))
        {
            return new AzCliInstallOption("Install with apt (Microsoft's install script)", "curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash");
        }

        // Fedora packages azure-cli itself; RHEL and its rebuilds need Microsoft's repository first.
        if (ids.Contains("fedora") && !ids.Contains("rhel"))
        {
            return new AzCliInstallOption("Install with dnf (Fedora package)", "sudo dnf install -y azure-cli");
        }

        if (ids.Overlaps(["rhel", "centos"]))
        {
            return new AzCliInstallOption(
                "Install with dnf (Microsoft repository)",
                "sudo rpm --import https://packages.microsoft.com/keys/microsoft.asc && sudo dnf install -y https://packages.microsoft.com/config/rhel/9.0/packages-microsoft-prod.rpm && sudo dnf install -y azure-cli");
        }

        if (ids.Overlaps(["opensuse", "suse", "sles"]))
        {
            return new AzCliInstallOption("Install with zypper", "sudo zypper install -y azure-cli");
        }

        if (ids.Contains("azurelinux") || ids.Contains("mariner"))
        {
            return new AzCliInstallOption("Install with tdnf", "sudo tdnf install -y azure-cli");
        }

        return hasHomebrew ? new AzCliInstallOption("Install with Homebrew", "brew install azure-cli") : null;
    }

    /// <summary>The <c>ID</c> and <c>ID_LIKE</c> values, e.g. {"ubuntu", "debian"}.</summary>
    private static HashSet<string> ReadDistributionIds(string osRelease)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in osRelease.Split('\n'))
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2 && parts[0].Trim() is "ID" or "ID_LIKE")
            {
                ids.UnionWith(parts[1].Trim().Trim('"').Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        }

        return ids;
    }
}
