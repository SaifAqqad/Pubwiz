namespace Pubwiz.WebDeploy;

/// <summary>Thrown when a Web Deploy request fails: bad credentials, a server-reported error, or a transport failure.</summary>
public sealed class WebDeployException : Exception
{
    public WebDeployException(string message, WebDeployServerError? serverError = null, Exception? inner = null)
        : base(message, inner)
        => ServerError = serverError;

    /// <summary>The decoded server error, when the failure carried an <c>MSDeploy.Exception</c> header.</summary>
    public WebDeployServerError? ServerError { get; }

    /// <summary>
    /// <c>true</c> when the server's TLS certificate failed validation; <see cref="WebDeploySyncOptions.AllowUntrusted"/>
    /// connects anyway.
    /// </summary>
    public bool IsUntrustedCertificate { get; init; }
}
