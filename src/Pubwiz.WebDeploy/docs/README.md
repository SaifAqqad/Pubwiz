# Web Deploy protocol notes

`Pubwiz.WebDeploy` is a cross-platform client for IIS Web Deploy (the `msdeploy.axd` handler of the Web
Management Service, WMSVC). Microsoft doesn't document the wire protocol, so it was reverse-engineered
from black-box captures. These notes record what was found, so the client can be understood, maintained
and ported without repeating the reverse-engineering.

| Document | What it covers |
|---|---|
| [protocol.md](protocol.md) | The WMSVC wire protocol: endpoint, sign-in, headers, the two-pass sync, the binary body and NRBF option headers, and error responses. This is what the client implements. |
| [msdeploy-exe.md](msdeploy-exe.md) | Driving the official `msdeploy.exe`, which produced the reference captures and which Pubwiz still uses on Windows: how it's located, its command line and quoting, and its output and errors. |
| [test-environment.md](test-environment.md) | The Windows + IIS + Web Deploy VM the captures came from, and how its TLS traffic was decrypted and split into messages. |

## How Pubwiz publishes to IIS

- Where the official `msdeploy.exe` is installed (Windows with Web Deploy), Pubwiz uses it.
- Everywhere else — Linux, macOS, or Windows without Web Deploy — it uses this client, which speaks the
  protocol directly over HTTPS. It authenticates with HTTP Basic (a user name and password); signing in
  as the current Windows user (NTLM) still needs `msdeploy.exe`.
- Azure App Service Web Deploy profiles go through Kudu's zip deploy instead, which works on every OS.

## Scope

The client performs the `iisApp` folder sync — the same operation as
`msdeploy -verb:sync -source:iisApp=<folder> -dest:iisApp=<site>`. It adds and updates files, creates
directories, and creates the IIS application when the destination is an application path. It also does what the
usual msdeploy switches do: delete extra files (or keep them), take the app offline (`AppOffline`), preview
(`-whatif`), compare by checksum (`-useCheckSum`, files up to 64 KB), skip rules (`-skip`) and the retry settings.
NTLM sign-in still needs `msdeploy.exe`.

## Prior art

No open-source Web Deploy client existed when this was written: searches of GitHub, npm and PyPI turned
up only wrappers around `msdeploy.exe`. `Microsoft.Web.Deployment` (the assembly behind `msdeploy.exe`)
targets .NET Framework on Windows only. Running `msdeploy.exe` under Mono or Wine relies on the Windows
registry and APIs and wasn't pursued; its license would need checking before shipping it.
