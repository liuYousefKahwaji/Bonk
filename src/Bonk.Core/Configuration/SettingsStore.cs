using System.Text.Json;
using System.Text.Json.Serialization;
using Bonk.Core.Models;

namespace Bonk.Core.Configuration;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        IgnoreReadOnlyProperties = true
    };
    public string FilePath { get; }
    public string? LoadWarning { get; private set; }

    public SettingsStore(string? filePath = null) => FilePath = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bonk", "config.json");

    public AppSettings Load()
    {
        LoadWarning = null;
        if (!File.Exists(FilePath)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options)
                ?? throw new JsonException("The settings file is empty.");
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Preserve the original before an autosave can replace it.
            var backup = FilePath + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmssfff");
            try { File.Copy(FilePath, backup); LoadWarning = $"Settings could not be loaded. Original saved at {backup}."; }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
            { LoadWarning = "Settings could not be loaded or backed up. Changes will not be saved this session."; }
            return new();
        }
    }

    public void Save(AppSettings settings)
    {
        if (LoadWarning?.Contains("will not be saved", StringComparison.Ordinal) == true)
            throw new IOException(LoadWarning);
        settings.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
