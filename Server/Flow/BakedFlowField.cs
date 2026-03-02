using System;

internal static class BakedFlowField
{
    private const byte PathableFlagMask = 1 << 0;
    private const float ZeroEpsilon = 0.000001f;

    private static readonly DBVector2[] DirectionLut = CreateDirectionLut();
    private static readonly byte[] CostBytes = BakedFlowFieldGeneratedData.CostBytes ?? Array.Empty<byte>();
    private static readonly byte[] Team0FlowBytes = BakedFlowFieldGeneratedData.Team0FlowBytes ?? Array.Empty<byte>();
    private static readonly byte[] Team1FlowBytes = BakedFlowFieldGeneratedData.Team1FlowBytes ?? Array.Empty<byte>();
    private static readonly int Team0PathableCells = CountPathableCells(Team0FlowBytes);
    private static readonly int Team1PathableCells = CountPathableCells(Team1FlowBytes);

    public static int CellSize => BakedFlowFieldGeneratedData.CellSize;
    public static int Width => BakedFlowFieldGeneratedData.Width;
    public static int Height => BakedFlowFieldGeneratedData.Height;
    public static int FieldSizeX => BakedFlowFieldGeneratedData.FieldSizeX;
    public static int FieldSizeY => BakedFlowFieldGeneratedData.FieldSizeY;
    public static int WorldOriginX => BakedFlowFieldGeneratedData.WorldOriginX;
    public static int WorldOriginZ => BakedFlowFieldGeneratedData.WorldOriginZ;

    public static bool IsConfigured
    {
        get
        {
            if (!TryGetExpectedCellCount(out var expectedCellCount))
            {
                return false;
            }

            return CostBytes.Length == expectedCellCount &&
                   Team0FlowBytes.Length == expectedCellCount &&
                   Team1FlowBytes.Length == expectedCellCount;
        }
    }

    public static bool HasTeamFlow(int team)
    {
        if (!IsConfigured)
        {
            return false;
        }

        return team switch
        {
            0 => Team0PathableCells > 0,
            1 => Team1PathableCells > 0,
            _ => false
        };
    }

    public static bool TryGetDirection(int team, float worldX, float worldZ, out DBVector2 direction)
    {
        direction = new DBVector2();

        if (!TryWorldToCell(worldX, worldZ, out var cellX, out var cellY))
        {
            return false;
        }

        if (!TryGetTeamFlowBytes(team, out var flowBytes))
        {
            return false;
        }

        var index = cellY * Width + cellX;
        var raw = flowBytes[index];
        var flags = (byte)((raw >> 4) & 0x0F);
        if ((flags & PathableFlagMask) == 0)
        {
            return false;
        }

        var directionIndex = raw & 0x0F;
        if ((uint)directionIndex >= (uint)DirectionLut.Length)
        {
            return false;
        }

        var decodedDirection = DirectionLut[directionIndex];
        if (decodedDirection.SqrMagnitude <= ZeroEpsilon)
        {
            return false;
        }

        direction = decodedDirection;
        return true;
    }

    private static bool TryWorldToCell(float worldX, float worldZ, out int cellX, out int cellY)
    {
        cellX = 0;
        cellY = 0;

        if (!IsConfigured || CellSize <= 0)
        {
            return false;
        }

        var relX = worldX - WorldOriginX;
        var relZ = worldZ - WorldOriginZ;
        cellX = (int)MathF.Floor(relX / CellSize);
        cellY = (int)MathF.Floor(relZ / CellSize);
        return (uint)cellX < (uint)Width && (uint)cellY < (uint)Height;
    }

    private static bool TryGetTeamFlowBytes(int team, out byte[] flowBytes)
    {
        flowBytes = Array.Empty<byte>();
        if (!IsConfigured)
        {
            return false;
        }

        switch (team)
        {
            case 0:
                if (Team0PathableCells <= 0)
                {
                    return false;
                }

                flowBytes = Team0FlowBytes;
                return true;
            case 1:
                if (Team1PathableCells <= 0)
                {
                    return false;
                }

                flowBytes = Team1FlowBytes;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetExpectedCellCount(out int expectedCellCount)
    {
        expectedCellCount = 0;
        if (CellSize <= 0 || Width <= 0 || Height <= 0)
        {
            return false;
        }

        if (Height > int.MaxValue / Width)
        {
            return false;
        }

        expectedCellCount = Width * Height;
        return true;
    }

    private static int CountPathableCells(byte[] flowBytes)
    {
        if (flowBytes.Length == 0)
        {
            return 0;
        }

        var count = 0;
        for (var i = 0; i < flowBytes.Length; i++)
        {
            var flags = (byte)((flowBytes[i] >> 4) & 0x0F);
            if ((flags & PathableFlagMask) != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static DBVector2[] CreateDirectionLut()
    {
        var directions = new DBVector2[16];

        for (var i = 0; i < 8; i++)
        {
            directions[i] = CreateDirection(i * 45f);
        }

        for (var i = 0; i < 8; i++)
        {
            directions[8 + i] = CreateDirection(22.5f + i * 45f);
        }

        return directions;
    }

    private static DBVector2 CreateDirection(float angleDegrees)
    {
        var angleRadians = angleDegrees * (MathF.PI / 180f);
        var x = MathF.Cos(angleRadians);
        var z = -MathF.Sin(angleRadians);
        var sqrLength = x * x + z * z;
        if (sqrLength <= ZeroEpsilon)
        {
            return new DBVector2();
        }

        var invLength = 1f / MathF.Sqrt(sqrLength);
        return new DBVector2(x * invLength, z * invLength);
    }
}
