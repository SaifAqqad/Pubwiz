using Pubwiz.Core.Processes;

namespace Pubwiz.Core.Secrets;

/// <summary>
/// Picks the secret store for the current OS.
/// </summary>
public static class SecretStoreFactory
{
    public static ISecretStore Create(IProcessRunner processRunner)
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialStore();
        }

        return OperatingSystem.IsMacOS()
            ? new MacKeychainStore(processRunner)
            : new LibSecretStore(processRunner);
    }
}
