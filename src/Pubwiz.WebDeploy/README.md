# Pubwiz.WebDeploy

A cross-platform client for IIS Web Deploy (the `msdeploy.axd` handler of the Web Management Service,
WMSVC). It deploys a folder to an IIS site or application from Linux, macOS or Windows, without
`msdeploy.exe`.

It speaks the WMSVC wire protocol directly — the same `-verb:sync -source:iisApp=<folder>
-dest:iisApp=<site>` operation the official client performs. The protocol is undocumented by Microsoft
and was reverse-engineered from black-box captures; see [docs/protocol.md](docs/protocol.md).

```csharp
using Pubwiz.WebDeploy;

var endpoint = WebDeployEndpoint.Create("https://iis.contoso.com:8172", "Default Web Site/api");
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

It adds new files, updates changed ones, creates directories and, when the destination is an
application path (`Site/app`), creates the application. Existing files not present in the source are
left in place.

## Scope

- Authentication: HTTP Basic only (not NTLM / current-Windows-user).
- It does not yet delete extra files on the server, take the app offline during deploy, or run
  what-if; for those, the official `msdeploy.exe` is still used on Windows.

Used by [Pubwiz](https://github.com/SaifAqqad/Pubwiz) (`dotnet pubwiz`), and usable on its own.
