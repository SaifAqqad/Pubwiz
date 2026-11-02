using Pubwiz.Core.Profiles;
using Pubwiz.Core.Publishing;
using Pubwiz.Core.Targets.WebDeploy;
using Pubwiz.Tool.Rendering;
using Pubwiz.WebDeploy;
using System.Xml;

namespace Pubwiz.Tool.Wizard;

/// <summary>
/// Before publishing with Web Deploy (IIS), checks the server's certificate and, when this machine doesn't trust it
/// (WMSVC is self-signed by default), asks whether to trust it anyway. Works for both msdeploy.exe and the built-in client.
/// </summary>
/// <remarks>
/// Answers are remembered for the run, like passwords: the wizard asks right after "Publish now?" (so Esc goes back to
/// the projects), and publishing then reuses the answer. Esc throws <see cref="PromptCancelledException"/>.
/// </remarks>
public sealed class CertificateTrustPrompt(IAnsiConsole console, PublishRunner runner)
{
    private enum Choice
    {
        TrustOnce,
        TrustAndSave,
        DoNotTrust
    }

    private readonly Dictionary<PublishProfile, bool?> _answers = new(ReferenceEqualityComparer.Instance);

    /// <returns>
    /// <c>true</c> to trust the server's certificate for this run, <c>false</c> not to, or <c>null</c> when there was
    /// nothing to ask: not a Web Deploy profile, the profile already allows untrusted certificates, or the certificate is trusted.
    /// </returns>
    public async Task<bool?> AskAsync(PublishProfile profile, CancellationToken cancellationToken)
    {
        if (runner.FindTarget(profile) is not WebDeployTarget target || target.GetSetupError(profile) is not null
            || WebDeployProfile.AllowsUntrustedCertificate(profile))
        {
            return null;
        }

        if (_answers.TryGetValue(profile, out var answer))
        {
            return answer;
        }

        var serviceUrl = WebDeployProfile.GetEndpoint(profile).ServiceUrl;
        var certificate = await console.Status().StartAsync($"Checking the certificate of {serviceUrl.Authority}...",
            _ => WebDeployCertificate.GetAsync(serviceUrl, cancellationToken));

        answer = certificate is { IsTrusted: false } ? Ask(profile, serviceUrl, certificate) : null;
        _answers[profile] = answer;
        return answer;
    }

    private bool Ask(PublishProfile profile, Uri serviceUrl, WebDeployCertificate certificate)
    {
        console.Write(Widgets.Warning($"The certificate of {serviceUrl.Authority} isn't trusted: {string.Join(", ", certificate.Problems)}."));
        console.WriteLine();
        console.MarkupLine($"[dim]  Issued to {certificate.Subject.EscapeMarkup()} by {certificate.Issuer.EscapeMarkup()}, expires {certificate.ExpiresOn:d}[/]");
        console.MarkupLine($"[dim]  Thumbprint {certificate.Thumbprint.EscapeMarkup()}[/]");

        var choice = console.Choose("Trust it anyway?", [Choice.TrustOnce, Choice.TrustAndSave, Choice.DoNotTrust], c => c switch
        {
            Choice.TrustOnce => "Yes, for this publish",
            Choice.TrustAndSave => $"Yes, and save it in the profile [dim]({ProfileProperties.AllowUntrustedCertificate})[/]",
            _ => "No [dim]· publishing will fail[/]"
        });

        if (choice == Choice.TrustAndSave)
        {
            Save(profile);
        }

        return choice != Choice.DoNotTrust;
    }

    /// <summary>Sets <c>AllowUntrustedCertificate</c> in the profile's .pubxml, leaving the rest of the file untouched.</summary>
    private void Save(PublishProfile profile)
    {
        if (profile.FilePath is null)
        {
            WarnNotSaved("it isn't saved in a file");
            return;
        }

        try
        {
            PubxmlSerializer.SetProperty(profile.FilePath, ProfileProperties.AllowUntrustedCertificate, "true");
            profile[ProfileProperties.AllowUntrustedCertificate] = "true";
            console.MarkupLine($"[dim]Saved in {DisplayText.Link(profile.FilePath)}[/]");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or XmlException)
        {
            WarnNotSaved(ex.Message);
        }
    }

    private void WarnNotSaved(string reason)
    {
        console.Write(Widgets.Warning($"The profile couldn't be updated ({reason}), so the certificate is trusted for this publish only."));
        console.WriteLine();
    }
}
