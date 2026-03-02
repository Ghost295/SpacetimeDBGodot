using Godot;

namespace SpacetimeDB.Game.FlowField;

public static class DirectionLUT
{
    public static readonly Vector2[] Directions = CreateDirections();

    private static Vector2[] CreateDirections()
    {
        Vector2[] directions = new Vector2[16];

        // Preserve legacy indices (0-7) for compatibility with existing data.
        for (int i = 0; i < 8; i++)
        {
            float angleRadians = Mathf.DegToRad(i * 45f);
            directions[i] = new Vector2(Mathf.Cos(angleRadians), -Mathf.Sin(angleRadians)).Normalized();
        }

        // Additional mid-angle directions (8-15) at 22.5° offsets.
        for (int i = 0; i < 8; i++)
        {
            float angleRadians = Mathf.DegToRad(22.5f + i * 45f);
            directions[8 + i] = new Vector2(Mathf.Cos(angleRadians), -Mathf.Sin(angleRadians)).Normalized();
        }

        return directions;
    }
}

