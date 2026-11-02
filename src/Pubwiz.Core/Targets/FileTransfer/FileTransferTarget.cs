using Pubwiz.Core.Dotnet;
using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Publishing.Steps;

namespace Pubwiz.Core.Targets.FileTransfer;

/// <summary>
/// Publishes to a staging folder, then uploads it with FTP/FTPS (<c>WebPublishMethod=FTP</c>)
/// or SFTP (<c>WebPublishMethod=Custom</c>, <c>PublishProvider=Sftp</c>).
/// </summary>
/// <remarks>Registered twice, once per protocol, so jobs show the right target.</remarks>
/// <param name="dotnet">The dotnet CLI.</param>
/// <param name="sftp"><c>true</c> for the SFTP target, <c>false</c> for FTP/FTPS.</param>
public sealed class FileTransferTarget(DotnetCli dotnet, bool sftp) : IPublishTarget
{
    public PublishTargetType Type => sftp ? PublishTargetType.Sftp : PublishTargetType.Ftp;

    public bool CanPublish(PublishProfile profile) => sftp ? IsSftp(profile) : profile.Method == PublishMethod.FTP;

    /// <summary>SFTP with a private key doesn't need a password (an unencrypted key, or an agent).</summary>
    public bool RequiresPassword(PublishProfile profile) => !sftp || profile[ProfileProperties.PubwizSshPrivateKeyFile] is null;

    public IReadOnlyList<PublishStep> CreateSteps(PublishRequest request)
    {
        var profile = request.Profile;
        var endpoint = FileTransferEndpoint.FromProfile(profile);
        var options = new UploadOptions(
            DeleteExtraFiles: IsTrue(profile[ProfileProperties.DeleteExistingFiles]),
            UseAppOffline: request.TakeAppOffline ?? IsTrue(profile[ProfileProperties.PubwizUseAppOffline]));

        var userName = profile[ProfileProperties.UserName] ?? string.Empty;
        var sshConnection = new SftpConnection(endpoint.Host, endpoint.Port, userName, request.Password, profile[ProfileProperties.PubwizSshPrivateKeyFile], request.Password);
        Func<IFileUploader> createUploader = endpoint.IsSftp
            ? () => new SftpUploader(sshConnection)
            : () => new FtpUploader(new FtpConnection(endpoint.Host, endpoint.Port, userName, request.Password ?? string.Empty, endpoint.FtpSecurity,
                PassiveMode: !string.Equals(profile[ProfileProperties.FtpPassiveMode], "false", StringComparison.OrdinalIgnoreCase),
                AllowUntrustedCertificate: IsTrue(profile[ProfileProperties.AllowUntrustedCertificate])));

        var steps = new List<PublishStep> { StagingPublish.CreateStep(dotnet, request) };
        if (endpoint.IsSftp && GetCommand(request, ProfileProperties.PubwizSshPreDeployCommand) is { } preDeploy)
        {
            steps.Add(new SshCommandStep("Run pre-deploy command (SSH)", sshConnection, preDeploy));
        }

        steps.Add(new UploadStep($"Upload ({(endpoint.IsSftp ? "SFTP" : "FTP")})", createUploader, endpoint.RemoteDirectory, options));
        if (endpoint.IsSftp && GetCommand(request, ProfileProperties.PubwizSshPostDeployCommand) is { } postDeploy)
        {
            steps.Add(new SshCommandStep("Run post-deploy command (SSH)", sshConnection, postDeploy));
        }

        steps.Add(new SetDestinationStep(profile[ProfileProperties.SiteUrlToLaunchAfterPublish] ?? endpoint.ToString()));
        return steps;
    }

    /// <summary>A pre- or post-deploy command from the profile, with <c>$(ProjectName)</c> and the like filled in.</summary>
    private static string? GetCommand(PublishRequest request, string property) =>
        request.Profile[property] is { } command ? ProfileVariables.Expand(command, request) : null;

    private static bool IsSftp(PublishProfile profile) =>
        profile.Method == PublishMethod.Custom && string.Equals(profile[ProfileProperties.PublishProvider], ProfileProperties.SftpProvider, StringComparison.OrdinalIgnoreCase);

    private static bool IsTrue(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
