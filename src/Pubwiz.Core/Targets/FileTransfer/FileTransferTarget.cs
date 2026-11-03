using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Publishing.Steps;
using Pubwiz.Core.Targets.ServerLogins;

namespace Pubwiz.Core.Targets.FileTransfer;

/// <summary>
/// Publishes to a staging folder, then uploads it with FTP/FTPS (<c>WebPublishMethod=FTP</c>)
/// or SFTP (<c>WebPublishMethod=Custom</c>, <c>PublishProvider=Sftp</c>).
/// </summary>
/// <remarks>
/// Registered twice, once per protocol, so jobs show the right target.
/// <para>
/// When the profile's server has a login saved in <see cref="ServerCredentialStore"/>, that user name and password are
/// used instead of the profile's (and instead of its SSH key).
/// </para>
/// </remarks>
/// <param name="dotnet">The dotnet CLI.</param>
/// <param name="servers">Logins saved per server.</param>
/// <param name="sftp"><c>true</c> for the SFTP target, <c>false</c> for FTP/FTPS.</param>
public sealed class FileTransferTarget(DotnetCli dotnet, ServerCredentialStore servers, bool sftp) : IPublishTarget
{
    public PublishTargetType Type => sftp ? PublishTargetType.Sftp : PublishTargetType.Ftp;

    public bool CanPublish(PublishProfile profile) => sftp ? IsSftp(profile) : profile.Method == PublishMethod.FTP;

    /// <summary>
    /// A saved server login brings its own password, and SFTP with a private key doesn't need one (an unencrypted key,
    /// or an agent).
    /// </summary>
    public bool RequiresPassword(PublishProfile profile) =>
        FindServerLogin(profile) is null && (!sftp || profile[ProfileProperties.PubwizSshPrivateKeyFile] is null);

    /// <summary>The login saved for the profile's server, which replaces the profile's; <c>null</c> when there's none.</summary>
    public SavedServerLogin? FindServerLogin(PublishProfile profile)
    {
        try
        {
            return SavedServerLogin.Find(servers, FileTransferEndpoint.FromProfile(profile).Host);
        }
        catch (ArgumentException)
        {
            return null; // No valid PublishUrl; CreateSteps reports it.
        }
    }

    public IReadOnlyList<PublishStep> CreateSteps(PublishRequest request)
    {
        var profile = request.Profile;
        var endpoint = FileTransferEndpoint.FromProfile(profile);
        var options = new UploadOptions(
            DeleteExtraFiles: IsTrue(profile[ProfileProperties.DeleteExistingFiles]),
            UseAppOffline: request.TakeAppOffline ?? IsTrue(profile[ProfileProperties.PubwizUseAppOffline]));

        var savedLogin = FindServerLogin(profile);
        var steps = new List<PublishStep> { StagingPublish.CreateStep(dotnet, request) };
        steps.AddRange(endpoint.IsSftp ? CreateSftpSteps(request, endpoint, options, savedLogin) : [CreateFtpUploadStep(request, endpoint, options, savedLogin)]);
        steps.Add(new SetDestinationStep(profile[ProfileProperties.SiteUrlToLaunchAfterPublish] ?? endpoint.ToString()));
        return steps;
    }

    private static UploadStep CreateFtpUploadStep(PublishRequest request, FileTransferEndpoint endpoint, UploadOptions options, SavedServerLogin? savedLogin)
    {
        var profile = request.Profile;
        var profileConnection = new FtpConnection(endpoint.Host, endpoint.Port, profile[ProfileProperties.UserName] ?? string.Empty, request.Password ?? string.Empty,
            endpoint.FtpSecurity,
            PassiveMode: !string.Equals(profile[ProfileProperties.FtpPassiveMode], "false", StringComparison.OrdinalIgnoreCase),
            AllowUntrustedCertificate: IsTrue(profile[ProfileProperties.AllowUntrustedCertificate]));

        return new UploadStep("Upload (FTP)", async (context, cancellationToken) =>
        {
            var connection = savedLogin is null
                ? profileConnection
                : profileConnection with { UserName = savedLogin.UserName, Password = await savedLogin.GetPasswordAsync(context, cancellationToken) };

            return new FtpUploader(connection);
        }, endpoint.RemoteDirectory, options);
    }

    /// <summary>The upload, with the profile's pre- and post-deploy commands around it (run over the same SSH login).</summary>
    private static List<PublishStep> CreateSftpSteps(PublishRequest request, FileTransferEndpoint endpoint, UploadOptions options, SavedServerLogin? savedLogin)
    {
        var profile = request.Profile;
        var profileConnection = new SftpConnection(endpoint.Host, endpoint.Port, profile[ProfileProperties.UserName] ?? string.Empty, request.Password,
            profile[ProfileProperties.PubwizSshPrivateKeyFile], request.Password);

        // A saved login signs in with its password, in place of the profile's user, password and key.
        async Task<SftpConnection> GetConnectionAsync(PublishContext context, CancellationToken cancellationToken) => savedLogin is null
            ? profileConnection
            : profileConnection with
            {
                UserName = savedLogin.UserName,
                Password = await savedLogin.GetPasswordAsync(context, cancellationToken),
                PrivateKeyFile = null,
                PrivateKeyPassphrase = null
            };

        var steps = new List<PublishStep>();
        if (GetCommand(request, ProfileProperties.PubwizSshPreDeployCommand) is { } preDeploy)
        {
            steps.Add(new SshCommandStep("Run pre-deploy command (SSH)", GetConnectionAsync, preDeploy));
        }

        steps.Add(new UploadStep("Upload (SFTP)", async (context, cancellationToken) => new SftpUploader(await GetConnectionAsync(context, cancellationToken)),
            endpoint.RemoteDirectory, options));

        if (GetCommand(request, ProfileProperties.PubwizSshPostDeployCommand) is { } postDeploy)
        {
            steps.Add(new SshCommandStep("Run post-deploy command (SSH)", GetConnectionAsync, postDeploy));
        }

        return steps;
    }

    /// <summary>A pre- or post-deploy command from the profile, with <c>$(ProjectName)</c> and the like filled in.</summary>
    private static string? GetCommand(PublishRequest request, string property) =>
        request.Profile[property] is { } command ? ProfileVariables.Expand(command, request) : null;

    private static bool IsSftp(PublishProfile profile) =>
        profile.Method == PublishMethod.Custom && string.Equals(profile[ProfileProperties.PublishProvider], ProfileProperties.SftpProvider, StringComparison.OrdinalIgnoreCase);

    private static bool IsTrue(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
