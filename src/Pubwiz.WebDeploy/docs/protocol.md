# The Web Deploy (WMSVC) protocol

Microsoft doesn't publish this protocol. Everything here was reverse-engineered from black-box tests
against IIS 10 with Web Deploy 4.0 (`msdeploy.exe` 7.1.2001), using the setup in
[test-environment.md](test-environment.md). It is enough to deploy a folder to an IIS site or
application with the `iisApp` provider — what `Pubwiz.WebDeploy` implements.

## Endpoint

- `https://<server>:8172/msdeploy.axd?site=<IIS site>`, served by the Web Management Service (WMSVC).
- WMSVC uses a self-signed certificate by default, so clients usually need to allow untrusted certificates.
- Azure App Service exposes the same handler at `https://<app>.scm.azurewebsites.net:443/msdeploy.axd?site=<app>`.

## Sign-in

1. The client sends `HEAD` (no credentials) and gets `401` with `WWW-Authenticate: Basic realm="WebManagementService"`.
2. It repeats the `HEAD` with `Authorization: Basic ...` and gets `200`, carrying `MSDeploy.Response: v1`,
   `MSDeploy.VersionMin`, `MSDeploy.VersionMax` and `MSDeploy.Serialization`.

Only Basic authentication is implemented. NTLM (current Windows user) was not reverse-engineered.

## Headers sent on every request

```
MSDeploy.VersionMin: 7.1.600.0
MSDeploy.VersionMax: 9.0.2001.0
MSDeploy.RequestUICulture: en-US
MSDeploy.RequestCulture: en-US
Version: 9.0.0.0
MSDeploy.Serialization: All        (HEAD probe)  /  Binary  (POSTs)
```

## Shape of a sync

A sync runs as two numbered **passes**, each paired with a progress long-poll that shares its
`MSDeploy.RequestId`:

| Request | Purpose |
|---|---|
| `POST`, `MSDeploy.Method: GetTraceStatus`, empty body | Long-poll for progress. The chunked response is XML: `<results>` with `<action .../>` messages and, after pass 1, `<incomplete objectId="N"/>` entries for the source objects whose **content** the server still needs. |
| `POST`, `MSDeploy.Method: Sync`, `MSDeploy.PassId: 1`, body = the source tree as **metadata only** | The server creates directories and empty files and works out what content it needs. Response: `200`, body `<results/>`, header `MSDeploy.ChangeSummary`. |
| `POST`, `MSDeploy.Method: Sync`, `MSDeploy.PassId: 2` (new `RequestId`), body = the tree again **plus the content** of the incomplete objects | Applies the file content. Same response shape. |

The trace long-poll must be in flight while the `Sync` with the same `RequestId` runs; the client
starts `GetTraceStatus`, sends the `Sync`, then reads the `incomplete` list from the trace response.

Extra headers on `Sync` requests:

- `MSDeploy.sid` (a GUID, constant for the whole run) and `User-Agent: <tool>;sid=<sid>;op=Sync`
- `Content-Type: application/msdeploy`
- `MSDeploy.ProviderOptions`, `MSDeploy.BaseOptions`, `MSDeploy.SyncOptions` (see below)

## The option headers = gzip + base64 of MS-NRBF objects

Each header is base64 of a gzip stream holding a .NET Binary Formatter (MS-NRBF) serialized object
(assembly `Microsoft.Web.Deployment, Version=9.0.0.0, PublicKeyToken=31bf3856ad364e35`):

| Header | Type | Contents |
|---|---|---|
| `MSDeploy.ProviderOptions` | `DeploymentProviderOptions` | `providerName=iisApp`, `path=<dest IIS path>`, empty `names`/`values`. **Varies per deploy** (the destination), so it is regenerated. |
| `MSDeploy.BaseOptions` | `DeploymentBaseOptions` | attempts, interval, trace level, link extensions, skip directives… — all constant, sent verbatim. |
| `MSDeploy.SyncOptions` | `DeploymentSyncOptions` | `whatIf`, `useChecksum`, `deleteDestination`, `appStoreEnabled`, the default `rules` XML, `declaredParameters` — all constant, sent verbatim. |

MS-NRBF is a published spec with an official reader (`System.Formats.Nrbf`). `Pubwiz.WebDeploy` writes
the one class it needs (`DeploymentProviderOptions`) by hand in `NrbfProviderOptionsWriter`; it never
uses `BinaryFormatter`. Error responses carry `MSDeploy.Exception` (NRBF `DeploymentException`), decoded
without BinaryFormatter by `WebDeployErrorDecoder`.

## The Sync body = msdeploy's binary object stream

All integers are little-endian. A "stream string" is an **Int32 byte-length** prefix followed by UTF-8
bytes (distinct from NRBF's 7-bit lengths). The body is:

1. **systemInfo section**: `u16 4`, `Int32 length`, then a `<systemInfo …>` XML block describing the
   source machine (OS, IIS version, installed components).
2. **parameters section**: `u16 10`, `Int32 length`, `<parameters />`.
3. **Preamble + provider scaffold** — a fixed tree of provider objects that never changes shape:
   `MSDeploy.iisApp → iisApp → (createApp, contentPath → dirPath)`. Each object carries a name, a
   resolver type name, optionally a gzip+base64 NRBF `DeploymentProviderOptions` "resolver options"
   blob (provider name + the **source** path), and typed attributes (`createApp` holds `isDest=false`,
   `managedRuntimeVersion`, `appExists`, … ; `dirPath` holds `securityDescriptor="D:"`, `attributes`).
   Only the source path and the regenerated blobs vary between deploys; the rest is constant and is
   stored as captured byte runs in `MsDeployProtocol`.
4. **The file/dir tree** under `dirPath`, depth-first.

### Object ids

Objects are numbered depth-first (a directory before its children). The scaffold takes ids 0–4
(`dirPath` = 4), so the first file/dir is 5. The server reports these ids back as `<incomplete>`.

### File and directory objects

The children list opens with a constant token `03 00 03 00 00 00` and the serializer then emits, per
child (opcodes are `u16`):

```
directory:  08 00 | Int32 id | Int32 parentId | str name | str secDesc("D:") | Int32 0
            | Int32 4 | Int32 attributes(0x10)   ... children ...   03 00 | Int32 id   (close)
file:       09 00 | Int32 id | Int32 parentId | str name
            | Int32 8  | Int64 length
            | Int32 4  | Int32 attributes(0x20)
            | Int32 8  | Int64 lastWriteTime        (DateTime.ToBinary of the UTC time — NOT a FILETIME)
            | str secDesc("D:") | Int32 0 | Int32 0
            | <01 | 03 00 | Int32 id>               when no content is sent (pass 1, or unchanged in pass 2)
            | <00 | 03 00 | Int32 id | 05 00 | Int32 id | (06 00 | Int32 len | bytes)* | 06 00 | Int32 0>
                                                     when content is sent (incomplete file in pass 2)
```

The whole children list is closed with `03 00 | Int32 4` (close `dirPath`). The `lastWriteTime` field
is the single sharpest gotcha: the server does `DateTime.FromBinary(value).ToFileTimeUtc()`, so a real
Win32 `FILETIME` is rejected with *"Not a valid Win32 FileTime"*.

## Verification

`Pubwiz.WebDeploy.Tests` regenerates the children region of real captures (single file, two files,
nested and deep folders, an update) **byte-for-byte** in both passes, and checks the
`DeploymentProviderOptions` NRBF against a captured header. The gzip'd blobs and the scaffold are not
byte-compared (gzip output is implementation-specific); correctness there is confirmed against a live
IIS server.
