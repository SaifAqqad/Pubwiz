# Pubwiz.WebDeploy

A cross-platform client for IIS Web Deploy (the `msdeploy.axd` handler of the Web Management Service,
WMSVC). It deploys a folder to an IIS site or application from Linux, macOS or Windows, without
`msdeploy.exe`.

It talks to WMSVC over HTTPS and performs the same operation as
`msdeploy -verb:sync -source:iisApp=<folder> -dest:iisApp=<site>`. There is no public specification for
the protocol, so the implementation is based on the traffic `msdeploy.exe` sends to a test server; see
[docs/protocol.md](docs/protocol.md).

```csharp
using Pubwiz.WebDeploy;

var endpoint = WebDeployEndpoint.Create("https://iis.example.com:8172", "Default Web Site/api");
var credentials = new WebDeployCredentials("deployuser", "••••••");
var options = new WebDeploySyncOptions
{
    AllowUntrusted = true, // WMSVC uses a self-signed certificate by default
    OnProgress = p => Console.WriteLine(p.Message),
};

var result = await new WebDeployClient()
    .SyncFolderAsync(endpoint, credentials, "./publish", options);

Console.WriteLine($"{result.ChangeCount} changes applied.");
```

To ask the user before trusting a certificate, check it first:

```csharp
var certificate = await WebDeployCertificate.GetAsync(endpoint.ServiceUrl);
if (certificate is { IsTrusted: false })
{
    Console.WriteLine($"Not trusted: {string.Join(", ", certificate.Problems)} ({certificate.Thumbprint})");
    // e.g. "it's self-signed, it's issued to WMSvc-SHA2-IIS01, not iis.example.com"
}
```

It adds new files, updates changed ones, creates directories and, when the destination is an
application path (`Site/app`), creates the application. Existing files not present in the source are
left in place unless `DeleteExtraFiles` is set.

## Options

`WebDeploySyncOptions` covers the msdeploy settings a folder deploy needs:

| Option | msdeploy equivalent | What it does |
|---|---|---|
| `TakeAppOffline` | `-enableRule:AppOffline` | Puts `app_offline.htm` in place while syncing, so a running ASP.NET app releases its files. |
| `DeleteExtraFiles` | default (off: `-enableRule:DoNotDeleteRule`) | Deletes files and folders on the server that aren't in the source. Off by default. |
| `WhatIf` | `-whatif` | Only reports what would change; the result's `WhatIf` is `true`. |
| `UseChecksum` | `-useCheckSum` | Compares files by content instead of size and date (see the limit below). |
| `SkipRules` | `-skip:…` | Leaves matching files or folders alone, entirely or for one action (e.g. never delete `uploads`). |
| `RetryAttempts`, `RetryInterval` | `-retryAttempts`, `-retryInterval` | How the server retries a failing operation, e.g. on a locked file. |
| `AllowUntrusted` | `-allowUntrusted` | Accepts the server's self-signed certificate. |

```csharp
var options = new WebDeploySyncOptions { TakeAppOffline = true, DeleteExtraFiles = true };
options.SkipRules.Add(new WebDeploySkipRule(WebDeploySkipObject.Directory, @"\\uploads$") { Action = WebDeploySkipAction.Delete });
```

## Limitations

- Authentication: HTTP Basic only (not NTLM / current Windows user).
- `UseChecksum` compares files up to 64 KB by checksum. Larger files are always sent and the server compares their
  content, so they're never skipped, and a checksum preview lists them as updates when they exist on the server.
- It has been tested against IIS 10 with Web Deploy 4.0. Other versions, and changes Microsoft makes to Web Deploy
  later, may behave differently. Try `WhatIf` or a test site before deploying to a site you care about.

Used by [Pubwiz](https://github.com/SaifAqqad/Pubwiz) (`dotnet pubwiz`), and usable on its own.

Pubwiz is an independent project, not affiliated with or endorsed by Microsoft. IIS, Web Deploy and Azure are
trademarks of Microsoft. The software is provided as is, under the terms in [LICENSE](https://github.com/SaifAqqad/Pubwiz/blob/main/LICENSE).
