# Web Deploy (WMSVC) protocol notes

Working notes for `Pubwiz.WebDeploy`, the cross-platform Web Deploy client. Microsoft doesn't publish this protocol, so everything here comes from black-box testing. The test setup is `tools/test-vm` (Windows Server 2025, IIS 10, Web Deploy 4.0 = msdeploy 7.1.2001), with the official `msdeploy.exe` (in the VM) going through `tools/test-vm/capture/relay.py`.

## How to capture
1. Start the relay inside the VM's container:
   ```bash
   podman exec -d pubwiz-iis sh -c 'python3 /storage/capture/relay.py /storage/capture/<run> /storage/capture/cert.pem /storage/capture/key.pem'
   ```
   Host path: `~/.local/share/pubwiz-vm/storage/capture/`.
2. In the VM (over SSH), run msdeploy against `https://host.lan:18172/msdeploy.axd?site=PubwizTest` with `-allowUntrusted` and `authType=Basic`.
3. Split the streams with `python3 tools/test-vm/capture/split_http.py <run folder>`.

## Endpoint and auth
- `https://<server>:8172/msdeploy.axd?site=<IIS site>`, self-signed certificate by default.
- The client first sends `HEAD` without credentials and gets `401` (`WWW-Authenticate: Basic realm="WebManagementService"`), then `HEAD` with `Authorization: Basic` and gets `200`. The 200 carries `MSDeploy.Response: v1`, `MSDeploy.VersionMin`, `MSDeploy.VersionMax` and `MSDeploy.Serialization: Binary`.
- A plain `GET` with valid credentials gets `404`.

## Headers on every request
```
MSDeploy.VersionMin: 7.1.600.0
MSDeploy.VersionMax: 9.0.2001.0
MSDeploy.RequestUICulture: en-US
MSDeploy.RequestCulture: en-US
Version: 9.0.0.0
MSDeploy.Serialization: Binary        (All on the HEAD probe)
```

## A sync (`-verb:sync -source:iisApp=C:\src -dest:iisApp=PubwizTest`)
It runs as numbered **passes**, all sharing one `MSDeploy.RequestId` (GUID), plus a parallel progress long-poll.

| Request | Purpose |
|---|---|
| `POST`, `MSDeploy.Method: GetTraceStatus`, same RequestId, empty body | Long-poll. The response (`text/html`, chunked) is XML: `<results><action operationType="AddChild" name="dirPath" path="PubwizTest\sub" message="Adding directory (PubwizTest\sub)." /><incomplete objectId="5" /><incomplete objectId="7" /></results>`. `incomplete` lists the source objects whose content the server still needs. |
| `POST`, `MSDeploy.Method: Sync`, `MSDeploy.PassId: 1`, body = source object tree (~5 KB here) | The server compares the tree with the destination and applies metadata-only changes (new directories). The response is `200`, 10-byte body, `MSDeploy.ChangeSummary` header. |
| `POST`, `MSDeploy.Method: Sync`, `MSDeploy.PassId: 2`, new RequestId, body = tree + contents of the incomplete objects | Applies the file changes. Same response shape. |

Extra headers on Sync requests:
- `MSDeploy.sid` (GUID, constant for the whole run)
- `User-Agent: MSDeployExe;sid=<sid>;op=Sync`
- `Content-Type: application/msdeploy`
- `Transfer-Encoding: chunked`
- `Expect: 100-continue`
- `MSDeploy.ProviderOptions`, `MSDeploy.BaseOptions`, `MSDeploy.SyncOptions`

### Option headers = gzip + base64 of .NET BinaryFormatter (MS-NRBF) objects
| Header | Serialized type (assembly `Microsoft.Web.Deployment, Version=9.0.0.0, PublicKeyToken=31bf3856ad364e35`) | Fields seen |
|---|---|---|
| `MSDeploy.ProviderOptions` | `Microsoft.Web.Deployment.DeploymentProviderOptions` | `providerName` = `iisApp`, `path` = `PubwizTest`, `names[]`, `values[]` |
| `MSDeploy.BaseOptions` | `Microsoft.Web.Deployment.DeploymentBaseOptions` | `attempts`, `interval`, `includeAcls`, `encryptPassword`, `traceLevel` (System.Diagnostics.TraceLevel), `serialization` = `Binary`, `linkExtensions` (CertificateExtension, ContentExtension, FrameworkConfigExtension, HttpCertConfigExtension), `skipDirectives` (DeploymentSkipDirectiveCollection: `ConfigProtectedData:True:objectName="^configProtectedData$"`), `defaultsProviderNames`, `defaultsSettingNames`, `defaultsSettingValues` |
| `MSDeploy.SyncOptions` | `Microsoft.Web.Deployment.DeploymentSyncOptions` | `whatIf`, `useChecksum`, `deleteDestination`, `appStoreEnabled`, `rules` (an XML string: `<rules><enable name="..."/>...<disable name="AppOffline"/>...</rules>`), `declaredParameters` (`<parameters />`) |
| `MSDeploy.ChangeSummary` (response) | NRBF, to decode | |
| `MSDeploy.Exception` (error response) | NRBF `DeploymentException`; type and message read by `WebDeployErrorDecoder` | |

MS-NRBF is a published Microsoft open specification, and .NET ships an official reader (`System.Formats.Nrbf`). The plan: a minimal NRBF **writer** for just these classes (records: SerializationHeader, BinaryLibrary, ClassWithMembersAndTypes, BinaryObjectString, MemberReference, ArraySingleString, ObjectNull, MessageEnd), checked against the captured bytes with `System.Formats.Nrbf`. Never BinaryFormatter itself.

### Sync body = msdeploy's own binary object stream (to decode)
Seen so far, in pass 1 (all integers little-endian, strings = Int32 byte length + UTF-8):
1. `04 00` then an Int32 length (`0x0A39`) and a `<systemInfo …>` XML block describing the client machine (OS, IIS version, installed IIS components).
2. `0A 00` + Int32 length + `<parameters />`.
3. The object tree, depth-first. Each object carries a type name (`MSDeploy.iisApp`, then `iisApp`, `createApp`, `contentPath`, `dirPath`, file names), a resolver type name (`Microsoft.Web.Deployment.DeploymentDefaultObjectResolver`, `CreateApplicationObjectResolver`, `DirPathObjectResolver`). Resolvers carry another gzip+base64 NRBF blob, plus a key attribute (`path` = `C:\pubwiz-src`) and typed attributes (`isDest` → `DeploymentObjectBooleanAttributeValue`, `attributes` → `DeploymentObjectFileAttributesEnumerationAttributeValue`, `securityDescriptor`, `managedRuntimeVersion`, …).
4. Pass 2 repeats the tree and adds the content of the `incomplete` objects.

Still to map: the object header fields (the short Int16/Int32 values between strings: flags, child counts, object ids), the attribute value encodings, how file contents are framed in pass 2, and the 10-byte response body.

## Next steps
1. Decode the object stream fully by capturing variations: one file vs two, different sizes, a nested folder, a changed file (update), a deleted file with `-enableRule:DoNotDeleteRule` off. Diff the bodies.
2. Write the NRBF writer and the object stream writer in `Pubwiz.WebDeploy`. Test them byte for byte against captured fixtures, with the server-dependent parts (machine name, timestamps) made deterministic.
3. Write `WebDeployClient.SyncFolderAsync(endpoint, credentials, localFolder, options)`: HEAD auth probe, pass loop driven by `GetTraceStatus` `incomplete` results, progress from `<action>` messages.
4. Test against the VM: add, update and delete files, app_offline, an app inside a site (`Site/app`), and a wrong password (expect 401).
