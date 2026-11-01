namespace Pubwiz.WebDeploy.Protocol;

/// <summary>
/// Constant byte sequences that make up the fixed parts of a Web Deploy <c>iisApp</c> sync,
/// captured from the official client (Web Deploy 4.0 / msdeploy 7.1.2001) and documented in
/// <c>docs/protocol.md</c>. These describe the provider object scaffold
/// (<c>MSDeploy.iisApp &#8594; iisApp &#8594; createApp + contentPath &#8594; dirPath</c>) and the
/// request option headers, none of which vary between deployments.
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
    /// The constant <c>MSDeploy.BaseOptions</c> header (gzip + base64 of an NRBF
    /// <c>DeploymentBaseOptions</c>). Carries no deployment-specific data, so it is sent verbatim.
    /// </summary>
    public const string BaseOptionsHeader =
        "H4sIAAAAAAAEAKVTbWsaQRDWVM/zJSaBhPZLQUI/ypHgS6LFD422tPRNUNIPhYZ1b5SN6+6xO2dqf3F/RKGdaaqGFEqge+zcMM/u7DPP7GaymUzmJw3+86jskPn8XklnvZ1idAOTKIZE29UCDNZrl+C8sqbXiU74q9f6qcbUQc9Aik7oem2YTrSSb2E1tnMwvcbpZNo4b7VF3Gg3odGqPKID3oxWHmGxTdd8aLrJ2ZloyVb7tNNowsl5J8/Eoy3fT8R3sOF7x70QHj4mSIf5Mm0JBRKBBH2oDIJbCl1WRuo0hhdS+z0w0q0SHArvb6yLS8RFwjtYgt714JTQ6pvgXFWtzPzlVwTDZfiqn6tkoBxIVEvwRzFMBRXkh84uVQzug1iAP1xHR4CozOx38Ohe8FLoFDwR3cllg1wQBGGYfXqrWjRQYmasRyV9NN4QY127DxBidJdi32rNnjXcd555mt85Vyn/g+7Fv89k8QtLpnp1lQl5F8+AcwQXygi3KgbkFwtsQjZFNiUyBwzkeCG347APDtVUSYGwkTOoELLft9Qgg9voLkWfvHKkGrVmTvBUzbZoldDHrxETzngPzBf+TyMutxRv2huwXsU9Lib883wOimuntHYY543BPpnnt4zoPiAlgXggUHTHLoWunVxThO9C7/iL/HvVs+PyLzd2r/ytAwAA";

    /// <summary>
    /// The constant <c>MSDeploy.SyncOptions</c> header (gzip + base64 of an NRBF
    /// <c>DeploymentSyncOptions</c>) with Web Deploy's default rule set.
    /// </summary>
    public const string SyncOptionsHeader =
        "H4sIAAAAAAAEAIWUbW/TMBDHu8EqoSEQfIJqr6cO6FaBtCJBHyCCtdWybpN45TqX1arjs87ORvaB+JpwSaYyVpc5Ulrlfr77+x7c2Go0Gr95lb/ler7Nrx8nShI6TH37BubtBKzGIgPj91vnQE6h6X1ovymf/VY/1z4n6BnIPQm935rmc63kNyjOcAmm13k7Tzvvj7oi6XQPoXO0U0Zq/w1wwQEGqwD3/saFkRPrOZhr8pbmzUL4KN3NHfQXIJcuz14loMHDAJxXRpTkS2Ft7JFgaMRcQ7JDuQb3OgGpBUEyFSQy3kGuPOpWtbbrc281n/D717PjasfHY6gctAzzvb2huVaEplR1LkiVljFSJrS6hb3WwQN6FUXdVqLWiU9zHCnNRMBk0BQZ5m7mgmZrp4g6SliK8kUQOEX0/5H3WaNcfhWUpbkeVAmcWKBKqdtAz4zLrUXykDy+o6+Fc0reKZ0SepD+lLMaQAmEBya5YSp3mzB0bqqFT/lUYSSK4j6aVF2NCLPu4Rl23q1DJ+DFsuxLprvr5lguIBMxqw0WLV4qG5lrzmoSY04yoKJEZuN+wMC9/AUMJ02HjZfT9e8z0rEUpo4rMbPCr0OXNnLCqkAdoivDg8B9Bt+F8xekPJypLKCaRw4MN5QsqsG6K9zwp3I+4PYBHRlu1EepkVB6YoZEWPd0otx9GMfo68ZaVfdfYmgkFdbzXVFXOYDUx63NkVnw9Hlh5CaH3AF1uQcqTYFgM1mmfww3QGUm3QboQpAZId3J5CkRXmxAOb2TNNXKhIzBtJXOlanPfFBfT82nfFm9OLar+4xtu38AePDcPMwFAAA=";
}
