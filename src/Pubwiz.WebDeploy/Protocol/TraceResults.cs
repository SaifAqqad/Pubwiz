using System.Xml.Linq;

namespace Pubwiz.WebDeploy.Protocol;

/// <summary>
/// The parsed body of a <c>GetTraceStatus</c> response: the progress actions the server reported, the object ids whose
/// content it still needs (the <c>&lt;incomplete&gt;</c> entries), and, in checksum mode, the checksum of the server's
/// copy of those files (<c>&lt;incomplete objectId="9" checksum="1956569880" /&gt;</c>).
/// </summary>
internal sealed record TraceResults(IReadOnlyList<WebDeployProgress> Actions, IReadOnlySet<int> IncompleteObjectIds, IReadOnlyDictionary<int, uint> ServerChecksums)
{
    public static TraceResults Parse(string xml)
    {
        var actions = new List<WebDeployProgress>();
        var incomplete = new HashSet<int>();
        var checksums = new Dictionary<int, uint>();

        // The response can be empty or whitespace while a long-poll is still pending.
        if (string.IsNullOrWhiteSpace(xml))
        {
            return new TraceResults(actions, incomplete, checksums);
        }

        var root = XElement.Parse(xml.TrimStart('﻿'));
        foreach (var element in root.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "action":
                    actions.Add(new WebDeployProgress(
                        (string?)element.Attribute("operationType") ?? "",
                        (string?)element.Attribute("name") ?? "",
                        (string?)element.Attribute("path") ?? "",
                        (string?)element.Attribute("message") ?? ""));
                    break;
                case "incomplete" when int.TryParse((string?)element.Attribute("objectId"), out var id):
                    incomplete.Add(id);
                    if (uint.TryParse((string?)element.Attribute("checksum"), out var checksum))
                    {
                        checksums[id] = checksum;
                    }

                    break;
            }
        }

        return new TraceResults(actions, incomplete, checksums);
    }
}
