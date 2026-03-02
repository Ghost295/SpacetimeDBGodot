namespace SpacetimeDB.Game.FlowField;

public static class FlowFlags
{
    [System.Flags]
    public enum IntegrationFlags : byte
    {
        None = 0,
        ActiveWaveFront = 1 << 0,
        LineOfSight = 1 << 1,
        Pathable = 1 << 2
    }

    [System.Flags]
    public enum FlowFieldFlags : byte
    {
        None = 0,
        Pathable = 1 << 0,
        HasLineOfSight = 1 << 1
    }
}

