using System.Text.Json;

namespace ISBSpecialForcesServer;
internal static class ISBDiagnostics
{
    private static readonly Lazy<bool> InitialSetting = new(ReadInitialSetting);
    private static bool? _configured;
    internal static bool Enabled { get => _configured ?? InitialSetting.Value; set => _configured = value; }

    private static bool ReadInitialSetting()
    {
        var folder = Path.GetDirectoryName(typeof(ISBDiagnostics).Assembly.Location);
        var path = Path.Combine(folder ?? string.Empty, "config.jsonc");
        if (!File.Exists(path))
            return false;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return document.RootElement.TryGetProperty("diagnosticLogs", out var value) && value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
