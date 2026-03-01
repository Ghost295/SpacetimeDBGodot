using Godot;

namespace SpacetimeDB.Game.VAT;

/// <summary>
/// Resource representing a complete VAT model.
/// Contains the mesh, VATInfo reference, and animation track definitions.
/// </summary>
[Tool, GlobalClass]
public partial class VATModel : Resource
{
    /// <summary>
    /// The VAT mesh resource.
    /// This is the mesh exported from Blender with VAT UV coordinates.
    /// </summary>
    [Export]
    public Mesh Mesh { get; set; }

    /// <summary>
    /// Reference to the VATInfo resource containing shared textures.
    /// Multiple VATModels can share the same VATInfo.
    /// </summary>
    [Export]
    public VATInfo VatInfo { get; set; }

    /// <summary>
    /// Animation tracks: x = start frame, y = end frame.
    /// Use values from your Blender project.
    /// </summary>
    [Export]
    public Godot.Collections.Array<Vector2I> AnimationTracks { get; set; } = new();

    /// <summary>
    /// Validates that all required resources are assigned.
    /// </summary>
    public bool IsValid()
    {
        return Mesh != null && VatInfo != null && VatInfo.IsValid() && AnimationTracks.Count > 0;
    }

    /// <summary>
    /// Gets the start and end frames for a specific animation track.
    /// </summary>
    public Vector2I GetTrack(int trackIndex)
    {
        if (AnimationTracks.Count == 0)
            return new Vector2I(0, 1);
        return AnimationTracks[trackIndex % AnimationTracks.Count];
    }

    /// <summary>
    /// Gets a descriptive string for debugging.
    /// </summary>
    public override string ToString()
    {
        return $"VATModel[Mesh:{Mesh?.ResourcePath ?? "null"}, Info:{VatInfo}, Tracks:{AnimationTracks.Count}]";
    }
}


