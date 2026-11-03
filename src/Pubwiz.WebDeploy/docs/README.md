# Web Deploy protocol notes

`Pubwiz.WebDeploy` is a cross-platform client for IIS Web Deploy (the `msdeploy.axd` handler of the Web
Management Service, WMSVC). There is no public specification for the protocol, so these notes describe what
`msdeploy.exe` sends to a test server and how the client reproduces it.

| Document | What it covers |
|---|---|
| [protocol.md](protocol.md) | The WMSVC protocol as the client uses it: endpoint, sign-in, headers, the two-pass sync, the request body, the option headers and error responses. |
| [msdeploy-exe.md](msdeploy-exe.md) | Running `msdeploy.exe`, which Pubwiz still uses on Windows: how it's located, its command line and quoting, and its output and errors. |
| [test-environment.md](test-environment.md) | The Windows + IIS + Web Deploy VM used for testing, and how its traffic was recorded. |

## How Pubwiz publishes to IIS

- Where `msdeploy.exe` is installed (Windows with Web Deploy), Pubwiz uses it.
- Everywhere else (Linux, macOS, or Windows without Web Deploy) it uses this client over HTTPS. The client
  signs in with HTTP Basic (a user name and password); signing in as the current Windows user (NTLM) still
  needs `msdeploy.exe`.
- Azure App Service Web Deploy profiles go through Kudu's zip deploy instead, which works on every OS.

## What the client does

The client performs the `iisApp` folder sync, the same operation as
`msdeploy -verb:sync -source:iisApp=<folder> -dest:iisApp=<site>`. It adds and updates files, creates
directories, and creates the IIS application when the destination is an application path. It also supports the
common msdeploy switches: delete extra files (or keep them), take the app offline (`AppOffline`), preview
(`-whatif`), compare by checksum (`-useCheckSum`, files up to 64 KB), skip rules (`-skip`) and the retry settings.
