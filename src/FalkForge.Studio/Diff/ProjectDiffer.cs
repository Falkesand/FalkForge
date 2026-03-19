using System.Text.Json;
using System.Text.Json.Nodes;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Diff;

public static class ProjectDiffer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static List<DiffEntry> Diff(StudioProject left, StudioProject right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftJson = JsonSerializer.Serialize(left, SerializerOptions);
        var rightJson = JsonSerializer.Serialize(right, SerializerOptions);

        var leftNode = JsonNode.Parse(leftJson);
        var rightNode = JsonNode.Parse(rightJson);

        var entries = new List<DiffEntry>();
        CompareNodes(leftNode, rightNode, "", entries);
        return entries;
    }

    private static void CompareNodes(JsonNode? left, JsonNode? right, string path, List<DiffEntry> entries)
    {
        if (left is null && right is null)
            return;

        if (left is null)
        {
            CollectAll(right!, path, DiffKind.Added, entries);
            return;
        }

        if (right is null)
        {
            CollectAll(left, path, DiffKind.Removed, entries);
            return;
        }

        if (left is JsonObject leftObj && right is JsonObject rightObj)
        {
            CompareObjects(leftObj, rightObj, path, entries);
            return;
        }

        if (left is JsonArray leftArr && right is JsonArray rightArr)
        {
            CompareArrays(leftArr, rightArr, path, entries);
            return;
        }

        if (left is JsonValue leftVal && right is JsonValue rightVal)
        {
            CompareValues(leftVal, rightVal, path, entries);
            return;
        }

        // Different node types (e.g., object vs array)
        entries.Add(new DiffEntry(path, left.ToJsonString(), right.ToJsonString(), DiffKind.Modified));
    }

    private static void CompareObjects(JsonObject left, JsonObject right, string path, List<DiffEntry> entries)
    {
        var allKeys = new HashSet<string>();
        foreach (var kvp in left)
            allKeys.Add(kvp.Key);
        foreach (var kvp in right)
            allKeys.Add(kvp.Key);

        foreach (var key in allKeys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var childPath = string.IsNullOrEmpty(path) ? key : $"{path}.{key}";
            var leftChild = left[key];
            var rightChild = right[key];
            CompareNodes(leftChild, rightChild, childPath, entries);
        }
    }

    private static void CompareArrays(JsonArray left, JsonArray right, string path, List<DiffEntry> entries)
    {
        var maxCount = Math.Max(left.Count, right.Count);

        for (var i = 0; i < maxCount; i++)
        {
            var childPath = $"{path}[{i}]";

            if (i >= left.Count)
            {
                CollectAll(right[i]!, childPath, DiffKind.Added, entries);
                continue;
            }

            if (i >= right.Count)
            {
                CollectAll(left[i]!, childPath, DiffKind.Removed, entries);
                continue;
            }

            CompareNodes(left[i], right[i], childPath, entries);
        }
    }

    private static void CompareValues(JsonValue left, JsonValue right, string path, List<DiffEntry> entries)
    {
        var leftStr = left.ToJsonString();
        var rightStr = right.ToJsonString();

        if (string.Equals(leftStr, rightStr, StringComparison.Ordinal))
            return;

        entries.Add(new DiffEntry(path, FormatValue(left), FormatValue(right), DiffKind.Modified));
    }

    private static void CollectAll(JsonNode node, string path, DiffKind kind, List<DiffEntry> entries)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var kvp in obj)
                {
                    var childPath = string.IsNullOrEmpty(path) ? kvp.Key : $"{path}.{kvp.Key}";
                    if (kvp.Value is not null)
                        CollectAll(kvp.Value, childPath, kind, entries);
                }
                break;

            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is not null)
                        CollectAll(arr[i]!, $"{path}[{i}]", kind, entries);
                }
                break;

            case JsonValue val:
                var formatted = FormatValue(val);
                entries.Add(kind == DiffKind.Added
                    ? new DiffEntry(path, null, formatted, kind)
                    : new DiffEntry(path, formatted, null, kind));
                break;
        }
    }

    private static string FormatValue(JsonValue value)
    {
        var element = value.GetValue<JsonElement>();
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => element.GetRawText()
        };
    }
}
