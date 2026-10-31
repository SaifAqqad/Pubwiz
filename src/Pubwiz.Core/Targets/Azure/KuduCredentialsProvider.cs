using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Targets.Azure.Kudu;

namespace Pubwiz.Core.Targets.Azure;

/// <summary>
/// Picks the Kudu credentials for a request: the profile's publishing credentials, or an Entra ID token from az.
/// </summary>
public sealed class KuduCredentialsProvider(AzCli azCli)
{
    public async Task<KuduCredentials> GetAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var profile = context.Request.Profile;
        if (!AzureProfile.UsesAzCli(profile))
        {
            var userName = profile[ProfileProperties.UserName]!;
            context.Log.Info($"Signing in to Kudu as {userName} (publishing credentials)");
            return new KuduCredentials.Basic(userName, context.Request.Password ?? string.Empty);
        }

        context.ReportDetail("getting a token from az...");
        try
        {
            var token = await azCli.GetAccessTokenAsync(cancellationToken);
            context.Log.AddSecret(token);
            context.Log.Info("Signing in to Kudu with an az access token");
            return new KuduCredentials.Bearer(token);
        }
        catch (AzCliException ex)
        {
            throw new PublishStepException($"Could not get an access token from az ({ex.Message}). Run 'az login' and try again.");
        }
    }
}
