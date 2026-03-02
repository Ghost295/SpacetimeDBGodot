using System;

internal readonly struct BakedCardPlacementGrid
{
    public BakedCardPlacementGrid(
        int gridIndex,
        byte team,
        string gridId,
        Fix64 centerX,
        Fix64 centerZ,
        int cellsX,
        int cellsZ,
        Fix64 worldSizeX,
        Fix64 worldSizeZ)
    {
        GridIndex = gridIndex;
        Team = team;
        GridId = gridId ?? string.Empty;
        CenterX = centerX;
        CenterZ = centerZ;
        CellsX = Math.Max(1, cellsX);
        CellsZ = Math.Max(1, cellsZ);
        WorldSizeX = worldSizeX;
        WorldSizeZ = worldSizeZ;
    }

    public int GridIndex { get; }
    public byte Team { get; }
    public string GridId { get; }
    public Fix64 CenterX { get; }
    public Fix64 CenterZ { get; }
    public int CellsX { get; }
    public int CellsZ { get; }
    public Fix64 WorldSizeX { get; }
    public Fix64 WorldSizeZ { get; }
    public Fix64 OriginX => CenterX - (WorldSizeX / Fix64.FromInt(2));
    public Fix64 OriginZ => CenterZ - (WorldSizeZ / Fix64.FromInt(2));
    public Fix64 CellSizeX => CellsX <= 0 ? Fix64.Zero : WorldSizeX / Fix64.FromInt(CellsX);
    public Fix64 CellSizeZ => CellsZ <= 0 ? Fix64.Zero : WorldSizeZ / Fix64.FromInt(CellsZ);
}

internal readonly struct BakedBuildingObstacle
{
    public BakedBuildingObstacle(Fix64 x, Fix64 z, Fix64 radius)
    {
        X = x;
        Z = z;
        Radius = radius;
    }

    public Fix64 X { get; }
    public Fix64 Z { get; }
    public Fix64 Radius { get; }
}

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
    private static readonly BakedCardPlacementGrid[] CardPlacementGrids = BuildCardPlacementGrids();
    private static readonly BakedBuildingObstacle[] BuildingObstacles = BuildBuildingObstacles();

    public static string BakeHashBase64 => BakedFlowFieldGeneratedData.BakeHashBase64;
    public static int CellSize => BakedFlowFieldGeneratedData.CellSize;
    public static int Width => BakedFlowFieldGeneratedData.Width;
    public static int Height => BakedFlowFieldGeneratedData.Height;
    public static int FieldSizeX => BakedFlowFieldGeneratedData.FieldSizeX;
    public static int FieldSizeY => BakedFlowFieldGeneratedData.FieldSizeY;
    public static int WorldOriginX => BakedFlowFieldGeneratedData.WorldOriginX;
    public static int WorldOriginZ => BakedFlowFieldGeneratedData.WorldOriginZ;
    public static int CardPlacementGridCount => CardPlacementGrids.Length;

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

    public static bool TryGetCardPlacementGrid(int gridIndex, out BakedCardPlacementGrid grid)
    {
        if ((uint)gridIndex < (uint)CardPlacementGrids.Length)
        {
            grid = CardPlacementGrids[gridIndex];
            return true;
        }

        grid = default;
        return false;
    }

    public static BakedBuildingObstacle[] GetBuildingObstacles()
    {
        return BuildingObstacles;
    }

    public static bool TryResolveCardWorldCenter(
        int gridIndex,
        int cellX,
        int cellZ,
        int cardSizeX,
        int cardSizeY,
        out FixVec2 center)
    {
        center = FixVec2.Zero;
        if (!TryGetCardPlacementGrid(gridIndex, out var grid))
        {
            return false;
        }

        var safeSizeX = Math.Max(1, cardSizeX);
        var safeSizeY = Math.Max(1, cardSizeY);
        if (cellX < 0 ||
            cellZ < 0 ||
            safeSizeX > grid.CellsX ||
            safeSizeY > grid.CellsZ ||
            cellX + safeSizeX > grid.CellsX ||
            cellZ + safeSizeY > grid.CellsZ)
        {
            return false;
        }

        var cellSizeX = grid.CellSizeX;
        var cellSizeZ = grid.CellSizeZ;
        if (cellSizeX <= Fix64.Zero || cellSizeZ <= Fix64.Zero)
        {
            return false;
        }

        var centerTwiceX = (2 * cellX) + safeSizeX;
        var centerTwiceZ = (2 * cellZ) + safeSizeY;
        var centerCellX = Fix64.FromInt(centerTwiceX) / Fix64.FromInt(2);
        var centerCellZ = Fix64.FromInt(centerTwiceZ) / Fix64.FromInt(2);
        var worldX = grid.OriginX + (centerCellX * cellSizeX);
        var worldZ = grid.OriginZ + (centerCellZ * cellSizeZ);
        center = new FixVec2(worldX, worldZ);
        return true;
    }

    private static BakedCardPlacementGrid[] BuildCardPlacementGrids()
    {
        var teams = BakedFlowFieldGeneratedData.CardGridTeams ?? Array.Empty<byte>();
        var ids = BakedFlowFieldGeneratedData.CardGridIds ?? Array.Empty<string>();
        var centerX = BakedFlowFieldGeneratedData.CardGridCenterX ?? Array.Empty<int>();
        var centerZ = BakedFlowFieldGeneratedData.CardGridCenterZ ?? Array.Empty<int>();
        var cellsX = BakedFlowFieldGeneratedData.CardGridCellsX ?? Array.Empty<int>();
        var cellsZ = BakedFlowFieldGeneratedData.CardGridCellsZ ?? Array.Empty<int>();
        var worldSizeX = BakedFlowFieldGeneratedData.CardGridWorldSizeX ?? Array.Empty<int>();
        var worldSizeZ = BakedFlowFieldGeneratedData.CardGridWorldSizeZ ?? Array.Empty<int>();
        var count = Math.Min(
            teams.Length,
            Math.Min(
                ids.Length,
                Math.Min(
                    centerX.Length,
                    Math.Min(
                        centerZ.Length,
                        Math.Min(cellsX.Length, Math.Min(cellsZ.Length, Math.Min(worldSizeX.Length, worldSizeZ.Length)))))));
        if (count <= 0)
        {
            return Array.Empty<BakedCardPlacementGrid>();
        }

        var result = new BakedCardPlacementGrid[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = new BakedCardPlacementGrid(
                i,
                teams[i],
                ids[i] ?? string.Empty,
                Fix64.FromInt(centerX[i]),
                Fix64.FromInt(centerZ[i]),
                cellsX[i],
                cellsZ[i],
                Fix64.FromInt(worldSizeX[i]),
                Fix64.FromInt(worldSizeZ[i]));
        }

        return result;
    }

    private static BakedBuildingObstacle[] BuildBuildingObstacles()
    {
        var centerX = BakedFlowFieldGeneratedData.BuildingCenterX ?? Array.Empty<int>();
        var centerZ = BakedFlowFieldGeneratedData.BuildingCenterZ ?? Array.Empty<int>();
        var radius = BakedFlowFieldGeneratedData.BuildingRadius ?? Array.Empty<int>();
        var count = Math.Min(centerX.Length, Math.Min(centerZ.Length, radius.Length));
        if (count <= 0)
        {
            return Array.Empty<BakedBuildingObstacle>();
        }

        var result = new BakedBuildingObstacle[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = new BakedBuildingObstacle(
                Fix64.FromInt(centerX[i]),
                Fix64.FromInt(centerZ[i]),
                Fix64.FromInt(Math.Max(0, radius[i])));
        }

        return result;
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
