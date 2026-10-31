using Pubwiz.Core.Platform;
using Pubwiz.Core.Processes;

namespace Pubwiz.Core.Maui;

/// <summary>
/// Creates Android signing keystores with the JDK's <c>keytool</c>.
/// </summary>
public sealed class AndroidKeystore(IProcessRunner processRunner)
{
    /// <summary>Environment variable keytool reads the password from (<c>-storepass:env</c>), so it's never on the command line.</summary>
    private const string PasswordVariable = "PUBWIZ_KEYSTORE_PASSWORD";

    /// <summary>
    /// <c>keytool</c> from <c>JAVA_HOME</c>, or from <c>PATH</c>; <c>null</c> when no JDK is installed.
    /// </summary>
    public static string? FindKeytool()
    {
        if (Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } javaHome)
        {
            var keytool = Path.Combine(javaHome, "bin", OperatingSystemInfo.ToExecutableName("keytool"));
            if (File.Exists(keytool))
            {
                return keytool;
            }
        }

        return ExecutableLocator.FindInPath("keytool");
    }

    /// <summary>
    /// Creates a keystore with one RSA 2048 key valid for 10000 days (what Google Play expects).
    /// The same password protects the keystore and the key, as MAUI's tooling assumes.
    /// </summary>
    /// <param name="keystorePath">The file to create.</param>
    /// <param name="alias">The key's alias.</param>
    /// <param name="password">At least 6 characters.</param>
    /// <param name="distinguishedName">The certificate owner, e.g. <c>CN=Contoso, O=Contoso, C=US</c>.</param>
    /// <param name="cancellationToken">Cancels keytool.</param>
    /// <exception cref="PrerequisiteMissingException">No JDK is installed.</exception>
    public async Task CreateAsync(string keystorePath, string alias, string password, string distinguishedName, CancellationToken cancellationToken = default)
    {
        var keytool = FindKeytool() ?? throw new PrerequisiteMissingException("keytool (JDK)", "Install a JDK (e.g. Microsoft Build of OpenJDK) or set JAVA_HOME.");

        var spec = new ProcessSpec(keytool,
        [
            "-genkeypair", "-keystore", keystorePath, "-alias", alias,
            "-keyalg", "RSA", "-keysize", "2048", "-validity", "10000",
            "-dname", distinguishedName,
            "-storepass:env", PasswordVariable, "-keypass:env", PasswordVariable
        ])
        {
            Environment = new Dictionary<string, string?> { [PasswordVariable] = password }
        };

        var result = await processRunner.RunAsync(spec, cancellationToken: cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"keytool failed: {(result.Error + result.Output).Trim()}");
        }
    }
}
