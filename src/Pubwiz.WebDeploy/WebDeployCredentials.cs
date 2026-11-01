using System.Text;

namespace Pubwiz.WebDeploy;

/// <summary>Credentials for the Web Management Service. Basic authentication is the only scheme supported.</summary>
/// <param name="UserName">The IIS Manager or Windows user name.</param>
/// <param name="Password">The password.</param>
public sealed record WebDeployCredentials(string UserName, string Password)
{
    /// <summary>The value for an HTTP <c>Authorization: Basic</c> header (without the scheme prefix).</summary>
    public string ToBasicToken() => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{UserName}:{Password}"));
}
