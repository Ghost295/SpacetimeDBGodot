using System;
using Godot;

namespace SpacetimeDB.Addons.FlowFieldBaker;

[Tool, GlobalClass]
public partial class CardPlacementGridMarker3D : Marker3D
{
    [Export(PropertyHint.Range, "0,1,1")]
    public byte Team { get; set; } = 0;

    [Export]
    public string GridId { get; set; } = string.Empty;

    public override void _EnterTree()
    {
        AddToGroup("CardPlacementGrid");
        AddToGroup(Team == 1 ? "CardPlacementGrid_Team1" : "CardPlacementGrid_Team0");

        if (string.IsNullOrWhiteSpace(GridId))
        {
            GridId = Name.ToString();
        }
    }
}
