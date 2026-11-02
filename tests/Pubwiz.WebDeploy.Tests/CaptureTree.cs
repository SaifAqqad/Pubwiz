using Pubwiz.WebDeploy.Protocol;

namespace Pubwiz.WebDeploy.Tests;

/// <summary>
/// Parses the dirPath children region of a captured sync body back into a <see cref="SourceDirectory"/>
/// (plus the set of file object ids that carried content), so the serializer can be driven with the
/// exact tree a real sync used and its output compared byte-for-byte.
/// </summary>
internal static class CaptureTree
{
    public static (SourceDirectory Root, HashSet<int> ContentIds) Parse(byte[] body)
    {
        var token = new byte[] { 3, 0, 3, 0, 0, 0 };
        var pos = IndexOf(body, token) + token.Length;
        var contentIds = new HashSet<int>();

        var rootChildren = new List<SourceNode>();
        var stack = new Stack<List<SourceNode>>();
        stack.Push(rootChildren);

        ushort U16() { var v = BitConverter.ToUInt16(body, pos); pos += 2; return v; }
        int I32() { var v = BitConverter.ToInt32(body, pos); pos += 4; return v; }
        long I64() { var v = BitConverter.ToInt64(body, pos); pos += 8; return v; }
        string Str() { var n = I32(); var s = System.Text.Encoding.UTF8.GetString(body, pos, n); pos += n; return s; }

        while (true)
        {
            var op = U16();
            if (op is 9) // file
            {
                var id = I32(); _ = I32(); var name = Str();
                _ = I32(); var size = I64();
                _ = I32(); _ = I32();        // attributes
                _ = I32(); var mtime = I64();
                _ = Str();                    // security descriptor
                _ = I32(); _ = I32();         // end-of-attributes
                var flag = body[pos++];
                _ = U16(); _ = I32();         // close
                byte[] content = [];
                if (flag == 0)
                {
                    contentIds.Add(id);
                    _ = U16(); _ = I32();     // begin content
                    using var buf = new MemoryStream();
                    while (true)
                    {
                        _ = U16(); var n = I32();
                        if (n == 0) break;
                        buf.Write(body, pos, n); pos += n;
                    }

                    content = buf.ToArray();
                }

                stack.Peek().Add(new SourceFile(name, size, mtime, () => new MemoryStream(content)));
            }
            else if (op is 8) // directory
            {
                var id = I32(); _ = I32(); var name = Str();
                _ = Str(); _ = I32(); _ = I32(); _ = I32(); // secdesc, reserved, attr marker, attr value
                _ = id;
                var children = new List<SourceNode>();
                stack.Peek().Add(new SourceDirectory(name, children));
                stack.Push(children);
            }
            else if (op is 3) // close
            {
                var id = I32();
                if (id == 4) break; // closed dirPath
                stack.Pop();
            }
            else
            {
                throw new InvalidOperationException($"Unexpected opcode {op} at {pos - 2}");
            }
        }

        return (new SourceDirectory("root", rootChildren), contentIds);
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }

            if (match) return i;
        }

        return -1;
    }
}
