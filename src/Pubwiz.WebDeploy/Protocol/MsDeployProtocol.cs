namespace Pubwiz.WebDeploy.Protocol;

/// <summary>
/// Constant byte sequences that make up the fixed parts of a Web Deploy <c>iisApp</c> sync,
/// captured from the official client (Web Deploy 4.0 / msdeploy 7.1.2001) and documented in
/// <c>docs/protocol.md</c>. These describe the provider object scaffold
/// (<c>MSDeploy.iisApp &#8594; iisApp &#8594; createApp + contentPath &#8594; dirPath</c>) and the
/// constant parts of the request option headers.
/// </summary>
internal static class MsDeployProtocol
{
    /// <summary>
    /// The nine constant byte runs of the provider scaffold, in order. Between them the serializer
    /// inserts the regenerated resolver-options blobs and the (length-prefixed) source path, per
    /// <see cref="MsDeployBodyWriter"/>. Captured verbatim; see docs/protocol.md.
    /// </summary>
    public static readonly byte[][] ScaffoldSegments =
    [
        System.Convert.FromBase64String("AgAAAAAA/////4gSDwAAAE1TRGVwbG95Lmlpc0FwcDgAAABNaWNyb3NvZnQuV2ViLkRlcGxveW1lbnQuRGVwbG95bWVudERlZmF1bHRPYmplY3RSZXNvbHZlcg=="),
        System.Convert.FromBase64String("AAAAAAAAAAAAAAAAAgABAAAAAAAAAEATBgAAAGlpc0FwcAYAAABDaGlsZDE4AAAATWljcm9zb2Z0LldlYi5EZXBsb3ltZW50LkRlcGxveW1lbnREZWZhdWx0T2JqZWN0UmVzb2x2ZXI="),
        System.Convert.FromBase64String("AAAAAAAAAAAEAAAAcGF0aA=="),
        System.Convert.FromBase64String("AAAAAAIAAAAAAAAAAwAAAAAAAgACAAAAAQAAAEADCQAAAGNyZWF0ZUFwcAkAAABjcmVhdGVBcHA4AAAATWljcm9zb2Z0LldlYi5EZXBsb3ltZW50LkNyZWF0ZUFwcGxpY2F0aW9uT2JqZWN0UmVzb2x2ZXIAAAAAAAAAAAQAAABwYXRo"),
        System.Convert.FromBase64String("AAAAAAIAAAAGAAAAaXNEZXN0AQAAAAA+AAAATWljcm9zb2Z0LldlYi5EZXBsb3ltZW50LkRlcGxveW1lbnRPYmplY3RCb29sZWFuQXR0cmlidXRlVmFsdWUAAAAAFQAAAG1hbmFnZWRSdW50aW1lVmVyc2lvbgAAAAAAAAAAAgAAABUAAABlbmFibGUzMkJpdEFwcE9uV2luNjQAAAAAAAAAAAIAAAATAAAAbWFuYWdlZFBpcGVsaW5lTW9kZQAAAAAAAAAAAgAAAA8AAABhcHBsaWNhdGlvblBvb2wAAAAAAAAAAAEAAAAJAAAAYXBwRXhpc3RzBAAAAFRydWUAAAAAAQAAAAAAAAACAAMAAAABAAAAQBMLAAAAY29udGVudFBhdGgLAAAAY29udGVudFBhdGg4AAAATWljcm9zb2Z0LldlYi5EZXBsb3ltZW50LkRlcGxveW1lbnREZWZhdWx0T2JqZWN0UmVzb2x2ZXI="),
        System.Convert.FromBase64String("AAAAAAAAAAAEAAAAcGF0aA=="),
        System.Convert.FromBase64String("AAAAAAIAAAAAAAAAAwABAAAAAwACAAAAAgAEAAAAAwAAAEATBwAAAGRpclBhdGgLAAAAY29udGVudFBhdGguAAAATWljcm9zb2Z0LldlYi5EZXBsb3ltZW50LkRpclBhdGhPYmplY3RSZXNvbHZlcg=="),
        System.Convert.FromBase64String("AAAAAAAAAAAEAAAAcGF0aA=="),
        System.Convert.FromBase64String("AAAAAAIAAAASAAAAc2VjdXJpdHlEZXNjcmlwdG9yAgAAAEQ6AAAAAAAAAAAZAAAAcGFyZW50U2VjdXJpdHlEZXNjcmlwdG9ycwAAAAAAAAAAAQAAAAoAAABhdHRyaWJ1dGVzBAAAABAAAABQAAAATWljcm9zb2Z0LldlYi5EZXBsb3ltZW50LkRlcGxveW1lbnRPYmplY3RGaWxlQXR0cmlidXRlc0VudW1lcmF0aW9uQXR0cmlidXRlVmFsdWUAAAAAAAAAAA=="),
    ];

    /// <summary>The source-machine description (systemInfo XML). <c>{0}</c> is the machine name.</summary>
    public const string SystemInfoTemplate =
        "<systemInfo osVersion=\"6.3\" winDir=\"C:\\WINDOWS\" machineName=\"{0}\" processorArchitecture=\"x64\" msdeployVersion=\"1.0\" buildVersion=\"7.1.2001.0154\"><iisSystemInfo iisMajorVersion=\"10\" iisMinorVersion=\"0\"><aspNetVersionInfo><aspNetVersion version=\"4.0.30319.0\" /></aspNetVersionInfo><installedComponentInfo><installedComponent name=\"ASP\" installed=\"False\" /><installedComponent name=\"ASPNET\" installed=\"False\" /><installedComponent name=\"Authorization\" installed=\"False\" /><installedComponent name=\"BasicAuthentication\" installed=\"False\" /><installedComponent name=\"CGI\" installed=\"False\" /><installedComponent name=\"ClientCertificateMappingAuthentication\" installed=\"False\" /><installedComponent name=\"CustomLogging\" installed=\"False\" /><installedComponent name=\"DefaultDocument\" installed=\"True\" /><installedComponent name=\"DigestAuthentication\" installed=\"False\" /><installedComponent name=\"DirectoryBrowse\" installed=\"True\" /><installedComponent name=\"FTPServer\" installed=\"False\" /><installedComponent name=\"HttpCompressionDynamic\" installed=\"False\" /><installedComponent name=\"HttpCompressionStatic\" installed=\"True\" /><installedComponent name=\"IPSecurity\" installed=\"False\" /><installedComponent name=\"IISCertificateMappingAuthentication\" installed=\"False\" /><installedComponent name=\"HttpTracing\" installed=\"False\" /><installedComponent name=\"HttpRedirect\" installed=\"False\" /><installedComponent name=\"HttpLogging\" installed=\"True\" /><installedComponent name=\"HttpErrors\" installed=\"True\" /><installedComponent name=\"ISAPIExtensions\" installed=\"False\" /><installedComponent name=\"ISAPIFilter\" installed=\"False\" /><installedComponent name=\"LoggingLibraries\" installed=\"False\" /><installedComponent name=\"Metabase\" installed=\"False\" /><installedComponent name=\"NetFxEnvironment\" installed=\"False\" /><installedComponent name=\"NetFxExtensibility\" installed=\"False\" /><installedComponent name=\"ODBCLogging\" installed=\"False\" /><installedComponent name=\"ProcessModel\" installed=\"True\" /><installedComponent name=\"RequestFiltering\" installed=\"True\" /><installedComponent name=\"RequestMonitor\" installed=\"False\" /><installedComponent name=\"ServerSideInclude\" installed=\"False\" /><installedComponent name=\"StaticContent\" installed=\"True\" /><installedComponent name=\"W3SVC\" installed=\"True\" /><installedComponent name=\"WindowsAuthentication\" installed=\"False\" /><installedComponent name=\"CertProvider\" installed=\"False\" /><installedComponent name=\"AppWarmUpBeta\" installed=\"False\" /><installedComponent name=\"dynamicIPRestrictionsBeta\" installed=\"False\" /></installedComponentInfo></iisSystemInfo></systemInfo>";

    /// <summary>The empty declared-parameters document that follows systemInfo in the stream.</summary>
    public const string Parameters = "<parameters />";

    /// <summary>
    /// The start of the <c>MSDeploy.SyncOptions</c> object (NRBF <c>DeploymentSyncOptions</c>): the serialization header,
    /// the assembly, and the class with its six members (whatIf, useChecksum, deleteDestination, appStoreEnabled, rules,
    /// declaredParameters). The member values follow; see <see cref="NrbfOptionsWriter"/>.
    /// </summary>
    public static readonly byte[] SyncOptionsPrefix = System.Convert.FromBase64String(
        "AAEAAAD/////AQAAAAAAAAAMAgAAAFtNaWNyb3NvZnQud2ViLmRlcGxveW1lbnQsIFZlcnNpb249OS4wLjAuMCwgQ3VsdHVyZT1uZXV0cmFsLCBQdWJsaWNLZXlUb2tlbj0zMWJmMzg1NmFkMzY0ZTM1BQEAAAAuTWljcm9zb2Z0LldlYi5EZXBsb3ltZW50LkRlcGxveW1lbnRTeW5jT3B0aW9ucwYAAAAGd2hhdElmC3VzZUNoZWNrc3VtEWRlbGV0ZURlc3RpbmF0aW9uD2FwcFN0b3JlRW5hYmxlZAVydWxlcxJkZWNsYXJlZFBhcmFtZXRlcnMAAAAAAQEBAQEBAgAAAA==");

    /// <summary>
    /// The start of the <c>MSDeploy.BaseOptions</c> object (NRBF <c>DeploymentBaseOptions</c>), up to its first member
    /// values: the retry attempts and interval.
    /// </summary>
    public static readonly byte[] BaseOptionsPrefix = System.Convert.FromBase64String(
        "AAEAAAD/////AQAAAAAAAAAMAgAAAFtNaWNyb3NvZnQud2ViLmRlcGxveW1lbnQsIFZlcnNpb249OS4wLjAuMCwgQ3VsdHVyZT1uZXV0cmFsLCBQdWJsaWNLZXlUb2tlbj0zMWJmMzg1NmFkMzY0ZTM1DAMAAABJU3lzdGVtLCBWZXJzaW9uPTQuMC4wLjAsIEN1bHR1cmU9bmV1dHJhbCwgUHVibGljS2V5VG9rZW49Yjc3YTVjNTYxOTM0ZTA4OQUBAAAALk1pY3Jvc29mdC5XZWIuRGVwbG95bWVudC5EZXBsb3ltZW50QmFzZU9wdGlvbnMLAAAACGF0dGVtcHRzCGludGVydmFsC2luY2x1ZGVBY2xzD2VuY3J5cHRQYXNzd29yZAp0cmFjZUxldmVsDXNlcmlhbGl6YXRpb24ObGlua0V4dGVuc2lvbnMOc2tpcERpcmVjdGl2ZXMVZGVmYXVsdHNQcm92aWRlck5hbWVzFGRlZmF1bHRzU2V0dGluZ05hbWVzFWRlZmF1bHRzU2V0dGluZ1ZhbHVlcwAAAAIEAQYEBgYGCAgBHVN5c3RlbS5EaWFnbm9zdGljcy5UcmFjZUxldmVsAwAAADpNaWNyb3NvZnQuV2ViLkRlcGxveW1lbnQuRGVwbG95bWVudFNraXBEaXJlY3RpdmVDb2xsZWN0aW9uAgAAAAIAAAA=");

    /// <summary>
    /// The constant middle of <c>MSDeploy.BaseOptions</c>, between the retry interval and the skip directives: no ACLs,
    /// trace level Info, Binary serialization, the four link extensions msdeploy enables, the skip directive collection
    /// and three empty "defaults" arrays.
    /// </summary>
    public static readonly byte[] BaseOptionsMiddle = System.Convert.FromBase64String(
        "AAoF/P///x1TeXN0ZW0uRGlhZ25vc3RpY3MuVHJhY2VMZXZlbAEAAAAHdmFsdWVfXwAIAwAAAAMAAAAGBQAAAAZCaW5hcnkJBgAAAAkHAAAACQgAAAAJCQAAAAkKAAAAEQYAAAAEAAAABgsAAAAUQ2VydGlmaWNhdGVFeHRlbnNpb24GDAAAABBDb250ZW50RXh0ZW5zaW9uBg0AAAAYRnJhbWV3b3JrQ29uZmlnRXh0ZW5zaW9uBg4AAAAXSHR0cENlcnRDb25maWdFeHRlbnNpb24FBwAAADpNaWNyb3NvZnQuV2ViLkRlcGxveW1lbnQuRGVwbG95bWVudFNraXBEaXJlY3RpdmVDb2xsZWN0aW9uAQAAAApkaXJlY3RpdmVzBgIAAAAJDwAAABEIAAAAAAAAABEJAAAAAAAAABEKAAAAAAAAAA==");
}
