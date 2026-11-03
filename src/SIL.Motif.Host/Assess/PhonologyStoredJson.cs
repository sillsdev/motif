using System.Text.Json.Nodes;

namespace SIL.Motif.Host.Assess;

/// <summary>Reads phonology JSON stored before PanGloss 0.14.0 dropped step caps and step counts.</summary>
public static class PhonologyStoredJson
{
    /// <summary>The same JSON without the step members, with the old step-cap reason named as the work cap.</summary>
    public static string DropRemovedStepMembers(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is null) return json;
        Visit(node);
        return node.ToJsonString();
    }

    private static void Visit(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                obj.Remove("stepCap");
                obj.Remove("steps");
                if (obj["reason"] is JsonValue reason && reason.TryGetValue<string>(out var text) && text == "stepCap")
                    obj["reason"] = "workCap";
                foreach (var child in obj.Select(pair => pair.Value).ToArray())
                    if (child is not null) Visit(child);
                break;
            case JsonArray array:
                foreach (var child in array)
                    if (child is not null) Visit(child);
                break;
        }
    }
}
