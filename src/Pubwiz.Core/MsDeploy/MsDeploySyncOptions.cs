using Pubwiz.WebDeploy;

namespace Pubwiz.Core.MsDeploy;

/// <summary>
/// What an <c>msdeploy -verb:sync</c> run copies, and where to.
/// </summary>
/// <param name="SourceDirectory">The published app folder.</param>
/// <param name="Destination">The Web Deploy server and IIS site/application.</param>
public sealed record MsDeploySyncOptions(string SourceDirectory, WebDeployEndpoint Destination)
{
    /// <summary>The user to sign in as; without one, msdeploy uses the current Windows account (NTLM).</summary>
    public string? UserName { get; init; }

    public string? Password { get; init; }

    /// <summary><c>Basic</c> or <c>NTLM</c>. Defaults to Basic with a user name (what Visual Studio uses for WMSVC), NTLM without.</summary>
    public string? AuthType { get; init; }

    /// <summary>Accept a self-signed server certificate (<c>-allowUntrusted</c>).</summary>
    public bool AllowUntrustedCertificate { get; init; }

    /// <summary>Leave files on the server that aren't part of the app (<c>DoNotDeleteRule</c>).</summary>
    public bool KeepExtraFiles { get; init; }

    /// <summary>Put an <c>app_offline.htm</c> in place while copying, so locked files can be replaced (<c>AppOffline</c> rule).</summary>
    public bool TakeAppOffline { get; init; }

    /// <summary>Log every provider operation (<c>-verbose</c>).</summary>
    public bool Verbose { get; init; }

    /// <summary>The auth type actually sent: the chosen one, or the default for whether a user name is set.</summary>
    public string EffectiveAuthType => AuthType ?? (UserName is null ? "NTLM" : "Basic");
}
