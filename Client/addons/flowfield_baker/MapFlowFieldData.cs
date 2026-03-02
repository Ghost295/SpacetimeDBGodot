using Godot;

namespace SpacetimeDB.Addons.FlowFieldBaker;

/// <summary>
/// Baked map flowfield payload written by the flowfield baker addon.
/// Stores core map metadata, costfield, and one precomputed flowfield per team.
/// </summary>
[Tool, GlobalClass]
public partial class MapFlowFieldData : Resource
{
    [Export]
    public int Version { get; set; } = 1;

    [Export(PropertyHint.Range, "1,256,1")]
    public int CellSize { get; set; } = 4;

    /// <summary>
    /// World-space field size in XZ units (same semantics as FimConfig.FieldSize).
    /// </summary>
    [Export]
    public Vector2I FieldSize { get; set; } = Vector2I.Zero;

    /// <summary>
    /// World origin used for world->cell mapping (same semantics as FimConfig.WorldOrigin).
    /// </summary>
    [Export]
    public Vector3I WorldOrigin { get; set; } = Vector3I.Zero;

    /// <summary>
    /// Goal cell used for Team0 flow solve.
    /// </summary>
    [Export]
    public Vector2I Team0GoalCell { get; set; }

    /// <summary>
    /// Goal cell used for Team1 flow solve.
    /// </summary>
    [Export]
    public Vector2I Team1GoalCell { get; set; }

    [Export]
    public Image CostfieldR8 { get; set; }

    [Export]
    public Image FlowTeam0R8 { get; set; }

    [Export]
    public Image FlowTeam1R8 { get; set; }

    /// <summary>
    /// SHA256(metadata_bytes || cost_bytes || team0_bytes || team1_bytes) base64.
    /// </summary>
    [Export]
    public string BakeHashBase64 { get; set; } = string.Empty;

    public bool TryGetGridSize(out int width, out int height)
    {
        width = 0;
        height = 0;

        if (CellSize <= 0)
            return false;
        if (FieldSize.X <= 0 || FieldSize.Y <= 0)
            return false;
        if (FieldSize.X % CellSize != 0 || FieldSize.Y % CellSize != 0)
            return false;

        width = FieldSize.X / CellSize;
        height = FieldSize.Y / CellSize;
        return true;
    }
}
