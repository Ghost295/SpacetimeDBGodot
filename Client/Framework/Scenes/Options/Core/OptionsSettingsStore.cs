using Godot;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace Framework.UI;

/// <summary>
/// Handles loading and saving ResourceOptions from/to options.json.
/// </summary>
internal sealed class OptionsSettingsStore
{
    private const string PathOptions = "user://options.json";
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public ResourceOptions Load()
    {
        if (FileAccess.FileExists(PathOptions))
        {
            using FileAccess file = FileAccess.Open(PathOptions, FileAccess.ModeFlags.Read);
            return JsonSerializer.Deserialize<ResourceOptions>(file.GetAsText()) ?? new();
        }

        return new ResourceOptions();
    }

    public void Save(ResourceOptions options)
    {
        string json = JsonSerializer.Serialize(options, _jsonOptions);
        using FileAccess file = FileAccess.Open(PathOptions, FileAccess.ModeFlags.Write);
        file.StoreString(json);
    }
}
