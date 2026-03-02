using System;

internal sealed class BattleCongestionField
{
    private static readonly (int Dx, int Dy, FixVec2 Dir)[] NeighborDirections =
    [
        (1, 0, new FixVec2(Fix64.One, Fix64.Zero)),
        (-1, 0, new FixVec2(-Fix64.One, Fix64.Zero)),
        (0, 1, new FixVec2(Fix64.Zero, Fix64.One)),
        (0, -1, new FixVec2(Fix64.Zero, -Fix64.One)),
        (1, 1, new FixVec2(Fix64.FromRatio(7071, 10000), Fix64.FromRatio(7071, 10000))),
        (1, -1, new FixVec2(Fix64.FromRatio(7071, 10000), -Fix64.FromRatio(7071, 10000))),
        (-1, 1, new FixVec2(-Fix64.FromRatio(7071, 10000), Fix64.FromRatio(7071, 10000))),
        (-1, -1, new FixVec2(-Fix64.FromRatio(7071, 10000), -Fix64.FromRatio(7071, 10000))),
    ];

    private readonly Fix64 _originX;
    private readonly Fix64 _originY;
    private readonly Fix64 _cellSize;
    private readonly Fix64 _invCellSize;
    private readonly int _width;
    private readonly int _height;

    private readonly int[] _teamA;
    private readonly int[] _teamB;

    public BattleCongestionField(BattleSimulationConfig config)
    {
        _originX = config.WorldMinX;
        _originY = config.WorldMinY;
        _cellSize = config.SpatialCellSize;
        _invCellSize = Fix64.One / _cellSize;

        var widthRaw = ((config.WorldMaxX - config.WorldMinX) / _cellSize).CeilToInt();
        var heightRaw = ((config.WorldMaxY - config.WorldMinY) / _cellSize).CeilToInt();
        _width = Math.Max(1, widthRaw + 1);
        _height = Math.Max(1, heightRaw + 1);

        var total = _width * _height;
        _teamA = new int[total];
        _teamB = new int[total];
    }

    public void Rebuild(BattleStateRuntime state)
    {
        Array.Clear(_teamA, 0, _teamA.Length);
        Array.Clear(_teamB, 0, _teamB.Length);

        for (var i = 0; i < state.UnitCount; i++)
        {
            if (state.States[i] == SimConstants.UnitDead)
            {
                continue;
            }

            if (!TryToCell(state.Positions[i], out var cx, out var cy))
            {
                continue;
            }

            var idx = ToIndex(cx, cy);
            if (state.Teams[i] == SimConstants.TeamA)
            {
                _teamA[idx]++;
            }
            else if (state.Teams[i] == SimConstants.TeamB)
            {
                _teamB[idx]++;
            }
        }
    }

    public int GetLocalDensity(byte team, FixVec2 position, int cellRadius)
    {
        if (!TryToCell(position, out var cx, out var cy))
        {
            return 0;
        }

        var source = team == SimConstants.TeamA ? _teamA : _teamB;
        var minX = Math.Max(0, cx - cellRadius);
        var maxX = Math.Min(_width - 1, cx + cellRadius);
        var minY = Math.Max(0, cy - cellRadius);
        var maxY = Math.Min(_height - 1, cy + cellRadius);

        var total = 0;
        for (var y = minY; y <= maxY; y++)
        {
            var row = y * _width;
            for (var x = minX; x <= maxX; x++)
            {
                total += source[row + x];
            }
        }

        return Math.Max(0, total - 1);
    }

    public FixVec2 SuggestDirection(byte team, FixVec2 position, FixVec2 fallbackDirection)
    {
        var fallback = fallbackDirection.Normalized();
        if (!TryToCell(position, out var cx, out var cy))
        {
            return fallback;
        }

        var friendly = team == SimConstants.TeamA ? _teamA : _teamB;
        var enemy = team == SimConstants.TeamA ? _teamB : _teamA;

        var bestDir = fallback;
        var bestScore = Fix64.FromInt(-100000);
        for (var i = 0; i < NeighborDirections.Length; i++)
        {
            var sampleX = cx + NeighborDirections[i].Dx;
            var sampleY = cy + NeighborDirections[i].Dy;
            if (sampleX < 0 || sampleX >= _width || sampleY < 0 || sampleY >= _height)
            {
                continue;
            }

            var idx = ToIndex(sampleX, sampleY);
            var enemyCount = enemy[idx];
            var friendlyCount = friendly[idx];
            var directional = FixVec2.Dot(NeighborDirections[i].Dir, fallback);
            var crowdScore =
                (Fix64.FromInt(enemyCount) * Fix64.FromInt(3))
                - (Fix64.FromInt(friendlyCount) * Fix64.FromInt(2));
            var score = crowdScore + (directional * Fix64.FromRatio(3, 2));
            if (score > bestScore)
            {
                bestScore = score;
                bestDir = NeighborDirections[i].Dir;
            }
        }

        if (bestScore <= Fix64.Zero && fallback.SqrMagnitude > Fix64.Epsilon)
        {
            return fallback;
        }

        return bestDir;
    }

    private bool TryToCell(FixVec2 position, out int x, out int y)
    {
        x = ((position.X - _originX) * _invCellSize).FloorToInt();
        y = ((position.Y - _originY) * _invCellSize).FloorToInt();
        return x >= 0 && x < _width && y >= 0 && y < _height;
    }

    private int ToIndex(int x, int y)
    {
        return (y * _width) + x;
    }
}
