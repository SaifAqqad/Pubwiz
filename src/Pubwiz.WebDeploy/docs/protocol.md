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
| `MSDeploy.BaseOptions` | `DeploymentBaseOptions` | retry attempts and interval, trace level, link extensions, **skip directives**… Generated: see [Sync options](#sync-options). |
| `MSDeploy.SyncOptions` | `DeploymentSyncOptions` | `whatIf`, `useChecksum`, `deleteDestination`, `appStoreEnabled`, the **rules** XML, `declaredParameters`. Generated: see [Sync options](#sync-options). |

MS-NRBF is a published spec with an official reader (`System.Formats.Nrbf`). `Pubwiz.WebDeploy` writes
`DeploymentProviderOptions` by hand in `NrbfProviderOptionsWriter`, and the two other objects in `NrbfOptionsWriter`
(captured constant byte runs around the values that vary); it never uses `BinaryFormatter`. Error responses carry `MSDeploy.Exception` (NRBF `DeploymentException`), decoded
without BinaryFormatter by `WebDeployErrorDecoder`.

## Sync options

Every msdeploy switch a folder deploy needs travels in those two headers; the body and the requests stay the same.
Each was found by capturing msdeploy.exe with and without the switch.

| msdeploy switch | Where | Encoding |
|---|---|---|
| `-whatif` | `SyncOptions.whatIf` | `true`. msdeploy then sends only pass 1, and the trace lists what *would* change. |
| `-useCheckSum` | `SyncOptions.useChecksum` | `true`, plus checksums in the body; see [Checksum mode](#checksum-mode). |
| `-enableRule:AppOffline` | `SyncOptions.rules` | `AppOffline` moves from the disabled to the enabled rules. The **server** then creates `app_offline.htm` before applying the changes and deletes it after (seen with a file watcher in the site folder). |
| `-enableRule:DoNotDeleteRule` | `SyncOptions.rules` | Same for `DoNotDeleteRule`. **msdeploy's default deletes** files on the server that aren't in the source. |
| `-skip:objectName=filePath,absolutePath=extra\.txt` | `BaseOptions` skip directives | One more string in the `directives` array: `CommandLineSkipDirective 1:True:objectName="filePath",absolutePath="extra\.txt"` (numbered from 1, after the constant `ConfigProtectedData:True:objectName="^configProtectedData$"`). |
| `-skip:skipAction=Delete,objectName=dirPath,absolutePath=\\logs$` | `SyncOptions.rules` | A skip that only prevents one action is a rule, first in the XML: `<skip skipAction="Delete" objectName="dirPath" absolutePath="\\logs$" xPath="" name="CommandLineSkipActionRule1" />`. |
| `-retryAttempts:7 -retryInterval:3000` | `BaseOptions.attempts`, `.interval` | Int32 values (defaults 5 and 1000 ms). |

`absolutePath` is a .NET regular expression the server compiles; an invalid one (e.g. `\uploads`, where `\u` needs four
hex digits) fails the sync with an exception from `Base64EncodingHelper.Deserialize`, so the client checks it first.

### The rules XML

msdeploy keeps 32 rules in one fixed order, each on or off, and writes the enabled ones first, then the disabled
ones, each group in that order (so turning a rule on moves it between the groups). The order and defaults, with `-`
for off: EnvironmentVariableNormalize, Parameterization, AboFilter, AnonymousUser, AppPoolIdentity, AppRootNormalize,
BlockHarmfulDeleteOperations, BlockUnsupportedDeleteOperations, ClassicAppPoolProtectRule, CreateApplicationRule,
CrossPlatformRule, -DoNotDeleteRule, -EncryptWebConfig, -IgnoreConfigInheritanceRule, IISConfigFrom64To32,
-IISSchemaDifferenceRule, MetakeyToIIS6, SchemaSection, SkipInvalidSource, -SkipNewerFilesRule, SkipUNC, SyncGeneral,
SyncXP, UrlScanSkipIncompat, -WarnForEncryptedDataRule, XpIsapis, IgnoreFileLastWriteTime, -AppOffline,
DependencyCheckAppPoolExists, DependencyCheckInUse, DependencyCheckFailOnError, -DependencyCheckFailOnWarning.
`SyncRules` rebuilds the XML from this list.

## Checksum mode

With `useChecksum`, the server compares files by a 32-bit checksum instead of size and date:

1. **Pass 1** is the usual metadata pass. The trace's `<incomplete>` entries now carry the checksum of the
   **server's** copy for files it already has: `<incomplete objectId="9" checksum="1956569880" />` (decimal).
   New files have no `checksum`.
2. **Pass 2:** for each incomplete file with a server checksum, the client computes its own. If they match, it sends
   the file record with the checksum and **no content**, and the server leaves the file alone; if not, the checksum and
   the content. New files get their content and checksum 0.
3. A checksum **preview** (`-whatif -useCheckSum`) has no second pass, so its only pass sends every file's content and
   checksum.

The checksum is the first Int32 after the file's security descriptor (otherwise 0); see the file record below.

### The checksum

msdeploy reads a file through a **64 KB buffer** and hashes the **whole buffer** each time, including stale bytes from
the previous read after a short last read (which is why every all-`a` file between 64 KB and 100 KB has the same
checksum). For a file that fits in one buffer (zero-padded), the checksum is linear over GF(2) and equals:

```
state = 0
for each byte b, from the LAST to the first:
    state = StepBack8(state) ^ ByteTable[b]
checksum = state ^ 0x7EE8CDCD                      (an empty file is 0xD7978EEB)
```

`StepBack` is the standard CRC-32 polynomial stepped backwards: a left-shifting register with feedback
`(0xEDB88320 << 1) | 1`. `ByteTable[b]` XORs one value per set bit: bit 0 adds `0xA7A443EC` (measured), and each next
bit `StepBack` of the previous one. Trailing zero bytes don't change the result (they're the buffer's padding); leading
ones do. This matches every file up to 65,536 bytes that was captured (`WebDeployChecksumTests`).

How the buffers of a **larger** file are combined is not known. Measured so far (two-buffer files; `z` = a 64 KB
buffer of zeros): the last buffer adds exactly its single-buffer value, `h(z,a) ⊕ h(z,z) = value(a)`, while the first
goes through a fixed linear map that is not a power of the CRC step, and the constant depends on the number of buffers
(`h(z,z) = 0xB66B2FCB`, `h(z,z,z) = 0xE20EEA22`). The client therefore sends larger files' content without a checksum.

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
            | str secDesc("D:") | UInt32 checksum (checksum mode, else 0) | Int32 0
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
`DeploymentProviderOptions` NRBF against a captured header. The `SyncOptions` and `BaseOptions` headers of every
option above are regenerated byte-for-byte from captures (`NrbfOptionsWriterTests`), and so is a checksum preview's
body. The gzip'd blobs and the scaffold are not
byte-compared (gzip output is implementation-specific); correctness there is confirmed against a live
IIS server.
