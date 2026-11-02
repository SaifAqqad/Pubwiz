# Driving msdeploy.exe

Pubwiz publishes to IIS on Windows by running the official `msdeploy.exe` (`src/Pubwiz.Core/Targets/WebDeploy`:
`MsDeployLocator`, `MsDeployCli`, `WebDeployTarget`). Everything below was verified against the test VM.

## Finding it

- The installer records its folder in `HKLM\SOFTWARE\Microsoft\IIS Extensions\MSDeploy\3`, value `InstallPath`
  (the same key the .NET SDK reads).
- The default location is `C:\Program Files\IIS\Microsoft Web Deploy V3\msdeploy.exe`.
- It isn't usually on `PATH`.

## The command

```
msdeploy.exe -verb:sync -source:iisApp="C:\publish"
  -dest:iisApp="Default Web Site/api",computerName="https://iis:8172/msdeploy.axd?site=Default%20Web%20Site",
        userName="deploy",password="...",authType="Basic"
  [-allowUntrusted] [-enableRule:DoNotDeleteRule] [-enableRule:AppOffline] [-useCheckSum] [-whatif]
  [-skip:skipAction=Delete,objectName=dirPath,absolutePath="\\uploads$"] [-verbose]
```

- `iisApp` deploys a folder into a site or application, and creates the application if it doesn't exist.
- Without `userName`, use `authType="NTLM"` to sign in as the current Windows account.
- `DoNotDeleteRule` keeps files on the server that aren't in the source (profile property `SkipExtraFilesOnServer`).
- `AppOffline` puts `app_offline.htm` in place during the sync and removes it afterwards (`EnableMSDeployAppOffline`).
- `-useCheckSum` compares files by checksum (`MSDeployUseChecksum`); `-whatif` only reports the changes.
- `-skip` leaves files or folders alone (Visual Studio's `MsDeploySkipRules` items); quoting the `absolutePath` value
  works like any other value.

## Command-line quoting

msdeploy.exe parses its raw command line itself, and the usual quoting breaks it:

- A whole argument in quotes (`"-dest:iisApp=Site A,..."`), which .NET's `ProcessStartInfo.ArgumentList` produces for
  values with spaces, is rejected: `Unrecognized argument`. Build the raw command line instead (`ProcessStartInfo.Arguments`).
- Quote each **value** instead: `iisApp="Default Web Site"`. Quotes keep spaces and commas inside the value.
- Without quotes, a comma ends the value (`password=a,b` makes `b` an unknown setting).
- There's no escape for `"` or `'` inside a value: doubling them doesn't work, and a single `'` acts as a quote and
  swallows the rest of the line.
- A backslash before the closing quote escapes it, so a value can't end with `\`.

`MsDeployCli` quotes every value and rejects values with `"`, `'` or a trailing `\`, naming the field (never the
value, which may be a password). msdeploy.exe has no way to read the password from stdin or a file, so the password
is visible in the process list while it runs.

## Output

- Progress lines: `Info: Adding file (PubwizTest\web.dll).`, `Info: Adding directory (...).`
- The last line on success: `Total changes: 12 (9 added, 3 deleted, 0 updated, 0 parameters changed, 106223 bytes copied)`.
- Exit code `0` on success, `-1` on failure.

## Errors

A failure prints:

```
Error Code: ERROR_USER_UNAUTHORIZED
More Information: Connected to the remote computer ("iis") using the Web Management Service, but could not authorize. ...  Learn more at: https://go.microsoft.com/fwlink/?LinkId=221672#ERROR_USER_UNAUTHORIZED.
Error: The remote server returned an error: (401) Unauthorized.
Error count: 1.
```

Codes seen: `ERROR_USER_UNAUTHORIZED` (wrong credentials), `ERROR_CERTIFICATE_VALIDATION_FAILED` (self-signed
certificate without `-allowUntrusted`), `ERROR_SITE_DOES_NOT_EXIST`. Parse errors (bad arguments) print only
`Error: ...` lines. `MsDeployCli.DescribeFailure` turns this into one line: the code plus the "More Information" text.
