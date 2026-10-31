# Web Deploy (WMSVC) protocol notes

Working notes for Pubwiz's cross-platform Web Deploy client. Microsoft doesn't publish this protocol, so everything here comes from black-box testing against the test VM (`tools/test-vm`: Windows Server 2025, IIS 10, Web Deploy 4.0).

## Endpoint
- `POST https://<server>:8172/msdeploy.axd?site=<IIS site name>`
- Served by the IIS Web Management Service (WMSVC) through HTTP.sys, behind a self-signed certificate by default (Visual Studio's "Allow untrusted certificate").
- HTTP Basic auth with a Windows account (or an IIS Manager user).
  - No credentials → `401`.
  - Valid credentials + `GET` → `404` (the handler only accepts POST).

## Request headers seen so far
| Header | Example | Notes |
|---|---|---|
| `Content-Type` | `application/msdeploy` | |
| `MSDeploy.VersionMin` | `7.1.600.0` | |
| `MSDeploy.VersionMax` | `9.0.1631.0` | |
| `MSDeploy.Method` | `Sync` | |
| `MSDeploy.RequestId` | GUID | |
| `MSDeploy.SyncOptions` | ? | **Required.** Without it the server answers 500: "The request header 'MSDeploy.SyncOptions' must be present before this request can be processed." |

## Error responses
- Status `500`, `Content-Type: application/msdeploy`, `MSDeploy.Response: v1`.
- `MSDeploy.Exception`: base64 of a **gzip** stream holding a .NET BinaryFormatter-serialized `Microsoft.Web.Deployment.DeploymentException` (assembly `Microsoft.Web.Delegation, Version=7.1.0.0`). The message and server stack trace can be read as plain UTF-8 strings inside the payload, so a full BinaryFormatter reader isn't needed to show errors.
- Server-side entry point seen in stack traces: `Microsoft.Web.Deployment.DeploymentAgent.HandleSync(asyncData, passId, user, siteName)`.

## Next steps
1. Run `msdeploy.exe -verb:sync -source:contentPath=... -dest:contentPath=PubwizTest,computerName=https://localhost:8172/msdeploy.axd?site=PubwizTest,...` **inside the VM** with its traffic routed through a logging proxy, and record the full exchange (headers, body format, multi-pass flow).
2. Document the body format of a sync request (the source object/archive stream) and the response stream (trace messages, results).
3. Write the minimal client: content sync of a publish folder (`contentPath` provider), `-allowUntrusted`, `skip` rules for `App_Data`, `app_offline.htm` support, and delete-extra-files behavior.
