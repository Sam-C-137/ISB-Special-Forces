using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ISBSpecialForces.Web;
public sealed record IsbDocument(string Id, string Name, string Category, string Format, string RelativePath, string Apply, string FilePath);
public sealed record IsbChange(string Path, JsonElement Value);
public sealed record IsbSaveRequest(string Id, string Revision, List<IsbChange> Changes);
public sealed class IsbConfigFiles(string modRoot, string? gameRoot, Action<IsbDocument, string>? validate = null)
{
    private static readonly object WriteLock = ISBSpecialForcesServer.ContainmentBreachConfigFile.WriteLock;
    public static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 128
    };
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        WriteIndented = true
    };
    public static string Revision(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static string Escape(string name) => name.Replace("~", "~0").Replace("/", "~1");
    public static string Unescape(string name) => name.Replace("~1", "/").Replace("~0", "~");
    public List<IsbDocument> Catalog()
    {
        var result = new List<IsbDocument>();
        void Add(string root, string rel, string name, string category, string apply)
        {
            var full = Path.GetFullPath(Path.Combine(root, rel));
            if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                return;
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                return;
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(category + "/" + rel)))[..20].ToLowerInvariant();
            result.Add(new(id, name, category, Path.GetExtension(full).Equals(".cfg", StringComparison.OrdinalIgnoreCase) ? "cfg" : "json", rel.Replace('\\', '/'), apply, full));
        }

        Add(modRoot, "config.jsonc", "ISB Deployment & Essentials\n", "Operations", "Restart the server and game to apply changes.");
        var containment = File.Exists(Path.Combine(modRoot, "containment_breach_config.jsonc")) ? "containment_breach_config.jsonc" : "containment_breach_config.json";
        Add(modRoot, containment, "Containment Breach", "Events", "Restart the server and game to apply changes.");
        if (gameRoot != null)
            Add(gameRoot, "BepInEx/config/com.samc137.isbsofextras.cfg", "Audio & event options", "Essentials", "Close the game before saving, then launch it again.");
        return result;
    }

    public IsbDocument Resolve(string id) => Catalog().SingleOrDefault(d => d.Id == id) ?? throw new ArgumentException("Configuration not found.");
    public object Read(string id)
    {
        var d = Resolve(id);
        var bytes = File.ReadAllBytes(d.FilePath);
        var text = Decode(bytes);
        if (bytes.Length > 4 * 1024 * 1024)
            throw new ArgumentException("Configuration file is too large.");
        if (d.Format == "cfg")
            return new
            {
                document = Public(d),
                revision = Revision(bytes),
                fields = ParseCfg(text),
                data = (JsonNode? )null
            };
        var data = JsonNode.Parse(text, documentOptions: JsonOptions) ?? throw new ArgumentException("Configuration is empty.");
        return new
        {
            document = Public(d),
            revision = Revision(bytes),
            data,
            fields = JsonHints(text)
        };
    }

    public static object Public(IsbDocument d) => new
    {
        d.Id,
        d.Name,
        d.Category,
        d.Format,
        d.RelativePath,
        d.Apply
    };
    private static string Decode(byte[] bytes) => Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
    public object Save(IsbSaveRequest request)
    {
        lock (WriteLock)
        {
            if (request.Changes is null || request.Changes.Count is < 1 or > 2000)
                throw new ArgumentException("Choose between 1 and 2000 changes.");
            var d = Resolve(request.Id);
            var old = File.ReadAllBytes(d.FilePath);
            if (!string.Equals(Revision(old), request.Revision, StringComparison.Ordinal))
                throw new IsbConflictException("The file was changed outside this page. Reload before saving.");
            var source = Decode(old);
            var text = d.Format == "cfg" ? PatchCfg(source, request.Changes) : PatchJson(source, request.Changes);
            validate?.Invoke(d, text);
            var encoded = Encoding.UTF8.GetBytes(text);
            if (old.AsSpan().StartsWith(Encoding.UTF8.Preamble))
                encoded = Encoding.UTF8.Preamble.ToArray().Concat(encoded).ToArray();
            var temp = d.FilePath + ".isb-web-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, encoded);
                if (Revision(File.ReadAllBytes(d.FilePath)) != request.Revision)
                    throw new IsbConflictException("The file changed while you were editing. Reload before saving.");
                File.Copy(d.FilePath, d.FilePath + ".isb-web.bak", true);
                File.Move(temp, d.FilePath, true);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }

            return new
            {
                revision = Revision(encoded),
                changes = request.Changes.Count,
                message = "Configuration saved. " + d.Apply
            };
        }
    }

    private static Dictionary<string, (int Start, int Length, JsonValueKind Kind)> Tokens(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var r = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 128 });
        var map = new Dictionary<string, (int, int, JsonValueKind)>();
        if (!r.Read())
            throw new ArgumentException("Empty JSON.");
        Walk(ref r, "", map);
        return map;
    }

    private static void Walk(ref Utf8JsonReader r, string path, Dictionary<string, (int, int, JsonValueKind)> map)
    {
        var start = (int)r.TokenStartIndex;
        var type = r.TokenType;
        var kind = type switch
        {
            JsonTokenType.StartObject => JsonValueKind.Object,
            JsonTokenType.StartArray => JsonValueKind.Array,
            JsonTokenType.String => JsonValueKind.String,
            JsonTokenType.Number => JsonValueKind.Number,
            JsonTokenType.True => JsonValueKind.True,
            JsonTokenType.False => JsonValueKind.False,
            _ => JsonValueKind.Null
        };
        if (type == JsonTokenType.StartObject)
        {
            while (r.Read() && r.TokenType != JsonTokenType.EndObject)
            {
                var name = r.GetString()!;
                r.Read();
                Walk(ref r, path + "/" + Escape(name), map);
            }
        }
        else if (type == JsonTokenType.StartArray)
        {
            var i = 0;
            while (r.Read() && r.TokenType != JsonTokenType.EndArray)
                Walk(ref r, path + "/" + i++, map);
        }

        map.Add(path, (start, (int)r.BytesConsumed - start, kind));
    }

    public static string PatchJson(string text, IReadOnlyList<IsbChange> changes)
    {
        var map = Tokens(text);
        var replacements = new List<(int Start, int Length, byte[] Data)>();
        var paths = new HashSet<string>();
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        using var original = JsonDocument.Parse(text, JsonOptions);
        foreach (var c in changes)
        {
            if (c.Path == "" || !paths.Add(c.Path) || paths.Any(p => p != c.Path && (p.StartsWith(c.Path + "/", StringComparison.Ordinal) || c.Path.StartsWith(p + "/", StringComparison.Ordinal))))
                throw new ArgumentException("Overlapping or duplicate changes.");
            if (!map.TryGetValue(c.Path, out var token))
                throw new ArgumentException("Unknown field: " + c.Path);
            var bools = token.Kind is JsonValueKind.True or JsonValueKind.False;
            var nullable = token.Kind == JsonValueKind.Null || c.Path.StartsWith("/difficulties/", StringComparison.Ordinal) && c.Value.ValueKind == JsonValueKind.Null;
            if (!nullable && (bools ? c.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) : token.Kind != c.Value.ValueKind))
                throw new ArgumentException("Invalid type: " + c.Path);
            ValidateValue(c.Path, c.Value);
            var previous = original.RootElement;
            foreach (var key in c.Path.Split('/').Skip(1))
                previous = previous.ValueKind == JsonValueKind.Array ? previous[int.Parse(key, CultureInfo.InvariantCulture)] : previous.GetProperty(Unescape(key));
            ValidateShape(previous, c.Value, c.Path);
            var value = JsonSerializer.Serialize(c.Value, OutputOptions).Replace("\n", newline, StringComparison.Ordinal);
            replacements.Add((token.Start, token.Length, Encoding.UTF8.GetBytes(value)));
        }

        var result = Encoding.UTF8.GetBytes(text);
        foreach (var p in replacements.OrderByDescending(p => p.Start))
            result = result[..p.Start].Concat(p.Data).Concat(result[(p.Start + p.Length)..]).ToArray();
        var output = FormatJson(Encoding.UTF8.GetString(result));
        using var check = JsonDocument.Parse(output, JsonOptions);
        ValidatePairs(check.RootElement, "");
        return output;
    }

    public static string FormatJson(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Allow, AllowTrailingCommas = true, MaxDepth = 128 });
        var lines = new List<(int Start, int Length, int Depth)>();
        var indent = "  ";
        var foundIndent = false;
        var lineStart = 0;
        var cursor = 0;
        while (reader.Read())
        {
            var start = (int)reader.TokenStartIndex;
            while (cursor < start)
            {
                if (bytes[cursor] == '\n')
                    lineStart = cursor + 1;
                cursor++;
            }

            if (!bytes.AsSpan(lineStart, start - lineStart).IsEmpty)
            {
                var whitespace = true;
                for (var i = lineStart; i < start; i++)
                    if (bytes[i] is not ((byte)' ' or (byte)'\t'))
                    {
                        whitespace = false;
                        break;
                    }

                if (!whitespace)
                    continue;
            }

            if (!foundIndent && reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.PropertyName && start > lineStart)
            {
                indent = Encoding.UTF8.GetString(bytes[lineStart..start]);
                foundIndent = true;
            }

            lines.Add((lineStart, start - lineStart, reader.CurrentDepth));
        }

        using var result = new MemoryStream();
        var copied = 0;
        foreach (var line in lines)
        {
            result.Write(bytes.AsSpan(copied, line.Start - copied));
            for (var depth = 0; depth < line.Depth; depth++)
                result.Write(Encoding.UTF8.GetBytes(indent));
            copied = line.Start + line.Length;
        }

        result.Write(bytes.AsSpan(copied));
        return Encoding.UTF8.GetString(result.ToArray());
    }

    private static void ValidateValue(string path, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in value.EnumerateObject())
                ValidateValue(path + "/" + Escape(p.Name), p.Value);
            return;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var v in value.EnumerateArray())
                ValidateValue(path + "/" + i++, v);
            return;
        }

        if (value.ValueKind != JsonValueKind.Number)
            return;
        if (!value.TryGetDouble(out var n) || !double.IsFinite(n))
            throw new ArgumentException("Invalid number: " + path);
        var name = path.Split('/').Last();
        if (name.Equals("chance", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Chance", StringComparison.OrdinalIgnoreCase) || name.EndsWith("ChancePercent", StringComparison.OrdinalIgnoreCase))
            if (n < 0 || n > 100)
                throw new ArgumentException("Chance must be between 0 and 100: " + path);
        if ((name is "weight" or "squadCount" || name.EndsWith("Multiplier", StringComparison.OrdinalIgnoreCase)) && n < 0)
            throw new ArgumentException("Value cannot be negative: " + path);
        if (name.EndsWith("DrawLimit", StringComparison.OrdinalIgnoreCase) && (n < -1 || n != Math.Truncate(n)))
            throw new ArgumentException("The limit accepts -1 or whole numbers starting at 0.");
    }

    private static void ValidateShape(JsonElement before, JsonElement after, string path)
    {
        var same = before.ValueKind == after.ValueKind || before.ValueKind is JsonValueKind.True or JsonValueKind.False && after.ValueKind is JsonValueKind.True or JsonValueKind.False;
        var nullable = before.ValueKind == JsonValueKind.Null || path.StartsWith("/difficulties/", StringComparison.Ordinal) && after.ValueKind == JsonValueKind.Null;
        if (!same && !nullable)
            throw new ArgumentException("Invalid type: " + path);
        if (before.ValueKind == JsonValueKind.Object && after.ValueKind == JsonValueKind.Object)
            foreach (var p in after.EnumerateObject())
                if (before.TryGetProperty(p.Name, out var old))
                    ValidateShape(old, p.Value, path + "/" + Escape(p.Name));
        if (before.ValueKind == JsonValueKind.Array && after.ValueKind == JsonValueKind.Array && before.GetArrayLength() > 0)
            for (var i = 0; i < after.GetArrayLength(); i++)
                ValidateShape(before[Math.Min(i, before.GetArrayLength() - 1)], after[i], path + "/" + i);
    }

    private static void ValidatePairs(JsonElement v, string path)
    {
        if (v.ValueKind == JsonValueKind.Object)
        {
            foreach (var(min, max)in new[]
            {
                ("min", "max"),
                ("raidTimeMin", "raidTimeMax")
            }

            )
                if (v.TryGetProperty(min, out var a) && v.TryGetProperty(max, out var b) && a.ValueKind == JsonValueKind.Number && b.ValueKind == JsonValueKind.Number && a.GetDouble() > b.GetDouble())
                    throw new ArgumentException("Minimum cannot exceed maximum: " + path);
            foreach (var p in v.EnumerateObject())
                ValidatePairs(p.Value, path + "/" + p.Name);
        }
        else if (v.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in v.EnumerateArray())
                ValidatePairs(item, path + "/" + i++);
        }
    }

    private static object JsonHints(string text)
    {
        var hints = new Dictionary<string, string>();
        var bytes = Encoding.UTF8.GetBytes(text);
        foreach (var(path, t)in Tokens(text))
        {
            var start = t.Start;
            var lineCount = 0;
            while (start > 0 && lineCount < 15)
            {
                start--;
                if (bytes[start] == 10)
                    lineCount++;
            }

            var prefix = Encoding.UTF8.GetString(bytes[start..t.Start]);
            var lines = prefix.Split('\n');
            var selected = new List<string>();
            for (var i = lines.Length - 2; i >= Math.Max(0, lines.Length - 14); i--)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("//"))
                    selected.Insert(0, line[2..].Trim());
                else if (line.Length > 0)
                    break;
            }

            if (selected.Count > 0)
                hints[path] = string.Join(" ", selected);
        }

        return hints;
    }

    public sealed record CfgField(string Path, string Section, string Name, string Type, string Value, string Description, string? Min, string? Max, string[] Choices);
    public static List<CfgField> ParseCfg(string text)
    {
        var result = new List<CfgField>();
        var section = "";
        var description = new List<string>();
        string type = "String";
        string? min = null, max = null;
        string[] choices = [];
        foreach (var line in text.Split('\n'))
        {
            var s = line.Trim();
            if (s.StartsWith('[') && s.EndsWith(']'))
            {
                section = s[1..^1];
                description.Clear();
                continue;
            }

            if (s.StartsWith("## "))
            {
                description.Add(s[3..]);
                continue;
            }

            if (s.StartsWith("# Setting type: "))
                type = s[16..].Trim();
            if (s.StartsWith("# Acceptable value range: "))
            {
                var m = Regex.Match(s, @"From ([^ ]+) to ([^ ]+)");
                if (m.Success)
                {
                    min = m.Groups[1].Value;
                    max = m.Groups[2].Value;
                }
            }

            if (s.StartsWith("# Acceptable values: "))
                choices = s[21..].Split(',').Select(x => x.Trim()).ToArray();
            if (s.StartsWith('#') || !s.Contains('='))
                continue;
            var eq = s.IndexOf('=');
            var name = s[..eq].Trim();
            result.Add(new("/" + Escape(section) + "/" + Escape(name), section, name, type, s[(eq + 1)..].Trim(), string.Join(" ", description), min, max, choices));
            description.Clear();
            type = "String";
            min = null;
            max = null;
            choices = [];
        }

        return result;
    }

    public static string PatchCfg(string text, IReadOnlyList<IsbChange> changes)
    {
        var fields = ParseCfg(text).ToDictionary(f => f.Path);
        var updated = new Dictionary<string, string>();
        foreach (var c in changes)
        {
            if (!fields.TryGetValue(c.Path, out var f) || updated.ContainsKey(c.Path))
                throw new ArgumentException("Unknown or duplicate field: " + c.Path);
            var value = c.Value.ValueKind == JsonValueKind.String ? c.Value.GetString()! : c.Value.GetRawText();
            if (value.IndexOfAny(['\r', '\n', '\0']) >= 0)
                throw new ArgumentException("Invalid value: " + f.Name);
            if (f.Type == "Boolean" && !bool.TryParse(value, out _))
                throw new ArgumentException("Use true or false: " + f.Name);
            if (f.Type is "Single" or "Double" or "Int32")
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || (f.Type == "Int32" && (n != Math.Truncate(n) || n < int.MinValue || n > int.MaxValue)) || (f.Type == "Single" && (n < float.MinValue || n > float.MaxValue)))
                    throw new ArgumentException("Invalid number: " + f.Name);
                if (f.Min != null && n < double.Parse(f.Min, CultureInfo.InvariantCulture) || f.Max != null && n > double.Parse(f.Max, CultureInfo.InvariantCulture))
                    throw new ArgumentException("Outside the allowed range: " + f.Name);
            }

            if (f.Choices.Length > 0 && !f.Choices.Contains(value))
                throw new ArgumentException("Invalid option: " + f.Name);
            updated[c.Path] = value;
        }

        var section = "";
        return Regex.Replace(text, @"(?m)^[^\r\n]*", m =>
        {
            var s = m.Value.Trim();
            if (s.StartsWith('[') && s.EndsWith(']'))
            {
                section = s[1..^1];
                return m.Value;
            }

            if (s.StartsWith('#') || !s.Contains('='))
                return m.Value;
            var eq = m.Value.IndexOf('=');
            var name = m.Value[..eq].Trim();
            return updated.TryGetValue("/" + Escape(section) + "/" + Escape(name), out var value) ? m.Value[..(eq + 1)] + " " + value : m.Value;
        });
    }
}

public sealed class IsbConflictException(string message) : Exception(message);
