using Godot;

namespace SpacetimeDB.Game.VAT;

/// <summary>
/// Resource containing shared VAT texture information.
/// Multiple VATModel resources can share the same VATInfo if they use the same textures.
/// </summary>
[Tool, GlobalClass]
public partial class VATInfo : Resource
{
    /// <summary>
    /// VAT offset map texture (vertex positions per frame).
    /// Typically exported as .exr from Blender.
    /// </summary>
    [Export]
    public Texture2D OffsetMap { get; set; }

    /// <summary>
    /// VAT normal map texture (vertex normals per frame).
    /// Typically exported as .png from Blender.
    /// </summary>
    [Export]
    public Texture2D NormalMap { get; set; }

    /// <summary>
    /// Albedo texture for the mesh.
    /// </summary>
    [Export]
    public Texture2D TextureAlbedo { get; set; }

    /// <summary>
    /// Validates that all required textures are assigned.
    /// </summary>
    public bool IsValid()
    {
        return OffsetMap != null && NormalMap != null && TextureAlbedo != null;
    }

    /// <summary>
    /// Gets a descriptive string for debugging.
    /// </summary>
    public override string ToString()
    {
        return $"VATInfo[Offset:{OffsetMap?.ResourcePath ?? "null"}, Normal:{NormalMap?.ResourcePath ?? "null"}, Albedo:{TextureAlbedo?.ResourcePath ?? "null"}]";
    }
}


