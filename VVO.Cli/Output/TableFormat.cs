using System.Globalization;
using System.Text;
using System.Text.Json;

namespace VVO.Cli.Output;

public enum OutputFormat
{
    Json,
    Table
}

/// <summary>
/// The human-readable view of a command's result. Each renderer reads the same JSON the command
/// would otherwise print, so the two views cannot say different things.
/// </summary>
public static class TableFormat
{
    public static string Volumes(JsonElement volumes) =>
        Table(
            ["ID", "NAME", "ICON", "COLOR", "FOLDERS"],
            volumes.EnumerateArray().Select(volume => new[]
            {
                Text(volume, "id"), Text(volume, "name"), Text(volume, "icon"), Text(volume, "color"),
                Text(volume, "folderCount")
            }));

    public static string Folders(JsonElement folders) =>
        Table(
            ["ID", "VOLUME", "TITLE", "SIZE", "RECORDS", "PATH"],
            folders.EnumerateArray().Select(folder => new[]
            {
                Text(folder, "id"), Text(folder, "volumeId"), Text(folder, "title"), Bytes(folder, "size"),
                Text(folder, "recordCount"), Text(folder, "path")
            }));

    public static string Listing(JsonElement listing)
    {
        var folder = listing.GetProperty("folder");
        var heading = PlacesOf(folder).FirstOrDefault() ?? Text(folder, "name");

        return heading + Environment.NewLine + Records(listing.GetProperty("children"));
    }

    public static string Tree(JsonElement tree)
    {
        var lines = new List<string> { PlacesOf(tree.GetProperty("folder")).FirstOrDefault() ?? "" };

        foreach (var entry in tree.GetProperty("below").EnumerateArray())
        {
            var record = entry.GetProperty("record");
            var name = Text(record, "name") + (record.GetProperty("isFolder").GetBoolean() ? "\\" : "");
            lines.Add(new string(' ', entry.GetProperty("depth").GetInt32() * 2) + name);
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string Search(JsonElement search)
    {
        var hits = search.GetProperty("hits");
        var table = Table(
            ["NAME", "SIZE", "PATH", "ID"],
            hits.EnumerateArray().Select(hit => new[]
            {
                Text(hit, "name"), hit.GetProperty("isFolder").GetBoolean() ? "" : Bytes(hit, "size"),
                PlacesOf(hit).FirstOrDefault() ?? "", Text(hit, "id")
            }));

        return search.GetProperty("truncated").GetBoolean()
            ? $"{table}{Environment.NewLine}{hits.GetArrayLength()} of {Text(search, "total")} hits shown."
            : table;
    }

    public static string Comparison(JsonElement comparison)
    {
        var counts = comparison.GetProperty("counts");
        var heading =
            $"{Text(counts, "added")} added, {Text(counts, "removed")} removed, {Text(counts, "changed")} changed";

        var rows = Table(
            ["STATUS", "PATH", "CHANGES"],
            comparison.GetProperty("rows").EnumerateArray().Select(row => new[]
            {
                Text(row, "status"),
                Text(row, "relativePath") is { Length: > 0 } path ? path : ".",
                string.Join(", ", row.GetProperty("changes").EnumerateArray().Select(change => change.GetString()))
                + (row.GetProperty("descendantCount").ValueKind == JsonValueKind.Number
                    ? $"{Text(row, "descendantCount")} inside"
                    : "")
            }));

        return heading + Environment.NewLine + rows;
    }

    /// <summary>
    /// For a result with no table of its own: each value on a line of its own, a list by its length.
    /// </summary>
    public static string Summary(JsonElement result)
    {
        var lines = new List<string>();
        Flatten(result, string.Empty, lines);
        return string.Join(Environment.NewLine, lines);
    }

    private static void Flatten(JsonElement element, string prefix, List<string> lines)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(property.Value, prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}", lines);
                }

                break;

            case JsonValueKind.Array:
                lines.Add($"{Label(prefix)}{element.GetArrayLength()} items");
                break;

            default:
                lines.Add($"{Label(prefix)}{Scalar(element)}");
                break;
        }
    }

    private static string Label(string prefix) => prefix.Length == 0 ? string.Empty : $"{prefix}: ";

    private static string Records(JsonElement records) =>
        Table(
            ["TYPE", "NAME", "SIZE", "MODIFIED", "ID"],
            records.EnumerateArray().Select(record => new[]
            {
                record.GetProperty("isFolder").GetBoolean() ? "folder" : "file",
                Text(record, "name"), Bytes(record, "size"), Text(record, "modified"), Text(record, "id")
            }));

    private static IEnumerable<string> PlacesOf(JsonElement record) =>
        record.GetProperty("entries").EnumerateArray().Select(entry => Text(entry, "cataloguePath"));

    private static string Text(JsonElement element, string name) => Scalar(element.GetProperty(name));

    private static string Scalar(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => string.Empty,
        JsonValueKind.String => element.GetString()!,
        _ => element.GetRawText()
    };

    private static string Bytes(JsonElement element, string name)
    {
        double size = element.GetProperty(name).GetInt64();
        string[] units = ["B", "KB", "MB", "GB", "TB"];

        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{size:0} B"
            : string.Create(CultureInfo.InvariantCulture, $"{size:0.#} {units[unit]}");
    }

    private static string Table(IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        var all = rows.ToList();
        if (all.Count == 0)
            return "Nothing to list.";

        var widths = headers
            .Select((header, column) => all.Select(row => row[column].Length).Append(header.Length).Max())
            .ToList();

        var text = new StringBuilder();
        foreach (var row in all.Prepend([.. headers]))
        {
            // The last column is left unpadded, so lines carry no trailing spaces
            var cells = row.Select((cell, column) => column == row.Length - 1 ? cell : cell.PadRight(widths[column]));
            text.AppendLine(string.Join("  ", cells));
        }

        return text.ToString().TrimEnd();
    }
}
