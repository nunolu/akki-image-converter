using System.Text.Json;

namespace Akki.ImageConverter.Core;

public enum UpdatePolicy { Notify, Automatic, Manual }
public enum AppTheme { System, Light, Dark }
public enum CompactEdge { Left, Right, Top, Bottom }

public sealed class Preferences
{
    public UpdatePolicy Updates { get; set; } = UpdatePolicy.Notify;
    public AppTheme Theme { get; set; } = AppTheme.System;
    public DateTimeOffset? LastUpdateCheck { get; set; }
    public ConversionOptions Conversion { get; set; } = new()
    {
        OutputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Akki Converted")
    };
    public bool IncludeSubfolders { get; set; }
    public CompactEdge CompactEdge { get; set; } = CompactEdge.Right;
    public Dictionary<string, ConversionOptions> Presets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "akki.lu", "ImageConverter", "settings.json");

    public static Preferences Load()
    {
        try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}
