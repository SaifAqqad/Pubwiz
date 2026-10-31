using System.IO.Compression;
using System.Text;

namespace Pubwiz.WebDeploy;

/// <summary>
/// A server-side Web Deploy error, decoded from the <c>MSDeploy.Exception</c> response header.
/// </summary>
/// <param name="ExceptionType">The server's exception class, e.g. <c>DeploymentException</c>.</param>
/// <param name="Message">The error message written by the server.</param>
public sealed record WebDeployServerError(string ExceptionType, string Message);

/// <summary>
/// Decodes the <c>MSDeploy.Exception</c> header: base64 of a gzip stream holding a .NET BinaryFormatter-serialized exception.
/// </summary>
/// <remarks>
/// BinaryFormatter is unsafe to deserialize and isn't needed here: the exception's fields are written in a fixed
/// order (ClassName, Message, ...) and each string is a "BinaryObjectString" record:
/// <c>0x06</c>, a 4-byte object id, a 7-bit encoded length, then UTF-8 bytes. Reading those records in order
/// gives the type name first and the message second.
/// </remarks>
public static class WebDeployErrorDecoder
{
    private const byte BinaryObjectStringRecord = 0x06;

    /// <returns>The error, or <c>null</c> when the header can't be decoded.</returns>
    public static WebDeployServerError? Decode(string headerValue)
    {
        byte[] payload;
        try
        {
            using var compressed = new MemoryStream(Convert.FromBase64String(headerValue));
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            gzip.CopyTo(decompressed);
            payload = decompressed.ToArray();
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            return null;
        }

        var strings = ReadStringRecords(payload).Take(2).ToList();
        return strings.Count == 2 ? new WebDeployServerError(strings[0], strings[1].Trim()) : null;
    }

    internal static IEnumerable<string> ReadStringRecords(byte[] payload)
    {
        var position = 0;
        while (position < payload.Length)
        {
            if (payload[position] == BinaryObjectStringRecord && TryReadString(payload, position + 1, out var value, out var next))
            {
                yield return value;
                position = next;
            }
            else
            {
                position++;
            }
        }
    }

    /// <summary>Reads "object id + length-prefixed UTF-8 string" at <paramref name="start"/>.</summary>
    private static bool TryReadString(byte[] payload, int start, out string value, out int next)
    {
        value = string.Empty;
        next = start;

        // Object ids are small positive numbers; anything else means this 0x06 byte was data, not a record.
        if (start + 4 >= payload.Length || BitConverter.ToInt32(payload, start) is <= 0 or > 1_000_000)
        {
            return false;
        }

        var position = start + 4;
        if (!TryRead7BitLength(payload, ref position, out var length) || length == 0 || position + length > payload.Length)
        {
            return false;
        }

        try
        {
            value = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(payload, position, length);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        next = position + length;
        return true;
    }

    /// <summary>The length format of <see cref="BinaryWriter.Write(string)"/>: 7 bits per byte, high bit = more bytes follow.</summary>
    private static bool TryRead7BitLength(byte[] payload, ref int position, out int length)
    {
        length = 0;
        for (var shift = 0; shift < 35 && position < payload.Length; shift += 7)
        {
            var current = payload[position++];
            length |= (current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                return true;
            }
        }

        return false;
    }
}
