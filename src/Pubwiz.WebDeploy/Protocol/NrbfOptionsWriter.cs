using System.Text;

namespace Pubwiz.WebDeploy.Protocol;

/// <summary>
/// Writes the <c>DeploymentSyncOptions</c> and <c>DeploymentBaseOptions</c> objects (MS-NRBF) that Web Deploy sends in the
/// <c>MSDeploy.SyncOptions</c> and <c>MSDeploy.BaseOptions</c> headers.
/// </summary>
/// <remarks>
/// Both objects have a fixed shape. Their constant parts (the class and member descriptions, and the members Pubwiz never
/// changes) are byte runs captured from msdeploy.exe, kept in <see cref="MsDeployProtocol"/>; this class writes the values
/// in between. The output matches captures of msdeploy.exe byte for byte; see docs/protocol.md.
/// </remarks>
internal static class NrbfOptionsWriter
{
    private const byte BinaryObjectString = 0x06;
    private const byte ArraySingleString = 0x11;
    private const byte MessageEnd = 0x0B;

    // Object ids in the captured layouts.
    private const int RulesId = 3, DeclaredParametersId = 4;
    private const int SkipDirectivesArrayId = 15, FirstSkipDirectiveId = 16;

    /// <summary>The skip directive msdeploy always sends: never sync encrypted IIS configuration.</summary>
    private const string DefaultSkipDirective = "ConfigProtectedData:True:objectName=\"^configProtectedData$\"";

    /// <returns>The raw MS-NRBF bytes of the <c>DeploymentSyncOptions</c> (uncompressed).</returns>
    public static byte[] WriteSyncOptions(WebDeploySyncOptions options)
    {
        return Write(writer =>
        {
            writer.Write(MsDeployProtocol.SyncOptionsPrefix);

            // The member values: whatIf, useChecksum, deleteDestination (wipe the whole destination, never used),
            // appStoreEnabled (always on in msdeploy), rules and declaredParameters.
            writer.Write(options.WhatIf);
            writer.Write(options.UseChecksum);
            writer.Write(false);
            writer.Write(true);
            WriteString(writer, RulesId, SyncRules.ToXml(options));
            WriteString(writer, DeclaredParametersId, MsDeployProtocol.Parameters);
        });
    }

    /// <returns>The raw MS-NRBF bytes of the <c>DeploymentBaseOptions</c> (uncompressed).</returns>
    public static byte[] WriteBaseOptions(WebDeploySyncOptions options)
    {
        // Skip rules without an action are "skip directives" here; the ones with an action are rules (see SyncRules).
        var directives = options.SkipRules
            .Where(rule => rule.Action is null)
            .Select((rule, i) => $"CommandLineSkipDirective {i + 1}:True:objectName=\"{rule.ObjectName}\",absolutePath=\"{rule.AbsolutePath}\"")
            .Prepend(DefaultSkipDirective)
            .ToList();

        return Write(writer =>
        {
            writer.Write(MsDeployProtocol.BaseOptionsPrefix);
            writer.Write(options.RetryAttempts);
            writer.Write((int)options.RetryInterval.TotalMilliseconds);
            writer.Write(MsDeployProtocol.BaseOptionsMiddle);

            writer.Write(ArraySingleString);
            writer.Write(SkipDirectivesArrayId);
            writer.Write(directives.Count);
            for (var i = 0; i < directives.Count; i++)
            {
                WriteString(writer, FirstSkipDirectiveId + i, directives[i]);
            }
        });
    }

    /// <exception cref="ArgumentException">
    /// A skip rule's path isn't a valid regular expression (the server fails with an unhelpful error then), or contains a
    /// double quote, which the directive can't hold.
    /// </exception>
    public static void Validate(WebDeploySyncOptions options)
    {
        foreach (var rule in options.SkipRules)
        {
            if (WebDeploySkipRule.GetPathError(rule.AbsolutePath) is { } error)
            {
                throw new ArgumentException(error, nameof(options));
            }
        }

        if (options.RetryAttempts < 0 || options.RetryInterval < TimeSpan.Zero)
        {
            throw new ArgumentException("The retry attempts and interval can't be negative.", nameof(options));
        }
    }

    private static byte[] Write(Action<BinaryWriter> writeBody)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), leaveOpen: true))
        {
            writeBody(writer);
            writer.Write(MessageEnd);
        }

        return stream.ToArray();
    }

    private static void WriteString(BinaryWriter writer, int objectId, string value)
    {
        writer.Write(BinaryObjectString);
        writer.Write(objectId);
        writer.Write(value); // BinaryWriter writes the 7-bit length prefix NRBF uses.
    }
}
