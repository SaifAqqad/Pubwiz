using System.Net.Http.Headers;
using System.Text;

namespace Pubwiz.Core.Targets.Azure.Kudu;

/// <summary>
/// How to authenticate to Kudu: the app's deployment credentials (Basic), or an Entra ID token from <c>az</c> (Bearer).
/// </summary>
public abstract record KuduCredentials
{
    public abstract AuthenticationHeaderValue ToHeader();

    /// <summary>Deployment ("publishing") credentials, e.g. from a .publishsettings file.</summary>
    public sealed record Basic(string UserName, string Password) : KuduCredentials
    {
        public override AuthenticationHeaderValue ToHeader() =>
            new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{UserName}:{Password}")));
    }

    /// <summary>An access token, e.g. from <c>az account get-access-token</c>.</summary>
    public sealed record Bearer(string AccessToken) : KuduCredentials
    {
        public override AuthenticationHeaderValue ToHeader() => new("Bearer", AccessToken);
    }
}
