namespace Pubwiz.WebDeploy.Protocol;

/// <summary>
/// The 32-bit file checksum msdeploy uses with <c>-useCheckSum</c>, for files up to 64 KB.
/// </summary>
/// <remarks>
/// Worked out from captures (see docs/protocol.md): msdeploy hashes files through a 64 KB buffer. For a file that fits
/// in one buffer the checksum is a CRC over the standard CRC-32 polynomial, folded from the last byte to the first:
/// <c>state = StepBack8(state) ^ ByteTable[b]</c>, then XOR <see cref="Offset"/>. Trailing zero bytes don't change it
/// (they're the buffer's padding). How msdeploy chains the checksums of several buffers wasn't worked out, so larger
/// files have no checksum here; the client sends their content, and the server compares that instead.
/// </remarks>
internal static class WebDeployChecksum
{
    /// <summary>The largest file whose checksum is known: one 64 KB buffer.</summary>
    public const int MaxSize = 64 * 1024;

    // The standard CRC-32 polynomial (reflected form), stepped backwards: as a left-shifting register its feedback
    // is (Polynomial << 1) | 1.
    private const uint Polynomial = 0xEDB88320;
    private const uint BackwardFeedback = (Polynomial << 1) | 1;

    /// <summary>XORed into the result; the checksum of a file holding only zero bytes.</summary>
    private const uint Offset = 0x7EE8CDCD;

    /// <summary>An empty file's checksum, which doesn't follow the formula (measured).</summary>
    private const uint EmptyFile = 0xD7978EEB;

    /// <summary>What the byte 0x01 adds (measured); the other single bits follow by stepping back.</summary>
    private const uint ByteOne = 0xA7A443EC;

    private static readonly uint[] ByteTable = BuildByteTable();
    private static readonly uint[] StepBack8Table = BuildStepBack8Table();

    /// <returns>The checksum, or <c>null</c> for a file over <see cref="MaxSize"/>.</returns>
    public static uint? Compute(SourceFile file)
    {
        if (file.Length > MaxSize)
        {
            return null;
        }

        using var stream = file.OpenRead();
        var content = new byte[file.Length];
        stream.ReadExactly(content);
        return Compute(content);
    }

    public static uint Compute(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
        {
            return EmptyFile;
        }

        uint state = 0;
        for (var i = content.Length - 1; i >= 0; i--)
        {
            state = (state << 8) ^ StepBack8Table[state >> 24] ^ ByteTable[content[i]];
        }

        return state ^ Offset;
    }

    private static uint StepBack(uint value) => (value & 0x80000000) != 0 ? (value << 1) ^ BackwardFeedback : value << 1;

    /// <summary>What each byte value adds: the XOR of its bits' contributions.</summary>
    private static uint[] BuildByteTable()
    {
        var bits = new uint[8];
        bits[0] = ByteOne;
        for (var k = 1; k < 8; k++)
        {
            bits[k] = StepBack(bits[k - 1]);
        }

        var table = new uint[256];
        for (var b = 0; b < 256; b++)
        {
            for (var k = 0; k < 8; k++)
            {
                if ((b >> k & 1) != 0)
                {
                    table[b] ^= bits[k];
                }
            }
        }

        return table;
    }

    /// <summary>Eight backward steps at once, for the register's top byte (the classic table-driven CRC trick).</summary>
    private static uint[] BuildStepBack8Table()
    {
        var table = new uint[256];
        for (uint top = 0; top < 256; top++)
        {
            var value = top << 24;
            for (var i = 0; i < 8; i++)
            {
                value = StepBack(value);
            }

            table[top] = value;
        }

        return table;
    }
}
