using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsInitializer.App;

internal sealed record AppSettings(
    [property: JsonPropertyOrder(0), JsonPropertyName("language")] string Language = "");

internal sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory,
        System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "WindowsInitializer") + ".json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path)) return new();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path, Encoding.UTF8), JsonOptions) ?? new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(AppSettings settings)
    {
        var temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(false));
        File.Move(temporary, Path, overwrite: true);
    }
}
