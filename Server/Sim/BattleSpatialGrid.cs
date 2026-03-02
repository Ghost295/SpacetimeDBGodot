using System;

internal sealed class BattleSpatialGrid
{
    private readonly int _gridWidth;
    private readonly int _gridHeight;
    private readonly Fix64 _cellSize;
    private readonly Fix64 _invCellSize;
    private readonly Fix64 _originX;
    private readonly Fix64 _originY;

    private readonly int[] _cellStart;
    private readonly int[] _cellCount;
    private readonly int[] _cellUnits;
    private readonly int[] _unitCells;

    public BattleSpatialGrid(
        Fix64 worldMinX,
        Fix64 worldMinY,
        Fix64 worldMaxX,
        Fix64 worldMaxY,
        Fix64 cellSize,
        int capacity)
    {
        _originX = worldMinX;
        _originY = worldMinY;
        _cellSize = cellSize;
        _invCellSize = Fix64.One / cellSize;

        var worldWidth = worldMaxX - worldMinX;
        var worldHeight = worldMaxY - worldMinY;
        _gridWidth = Math.Max(1, worldWidth.CeilToInt() / Math.Max(1, cellSize.CeilToInt()) + 1);
        _gridHeight = Math.Max(1, worldHeight.CeilToInt() / Math.Max(1, cellSize.CeilToInt()) + 1);

        var totalCells = _gridWidth * _gridHeight;
        _cellStart = new int[totalCells];
        _cellCount = new int[totalCells];
        _cellUnits = new int[Math.Max(1, capacity)];
        _unitCells = new int[Math.Max(1, capacity)];
    }

    public void Rebuild(BattleStateRuntime state)
    {
        var totalCells = _gridWidth * _gridHeight;
        Array.Clear(_cellCount, 0, totalCells);

        for (var i = 0; i < state.UnitCount; i++)
        {
            if (state.States[i] == SimConstants.UnitDead)
            {
                _unitCells[i] = -1;
                continue;
            }

            var cell = GetCellIndex(state.Positions[i].X, state.Positions[i].Y);
            if (cell >= 0 && cell < totalCells)
            {
                _unitCells[i] = cell;
                _cellCount[cell]++;
            }
            else
            {
                _unitCells[i] = -1;
            }
        }

        var offset = 0;
        for (var c = 0; c < totalCells; c++)
        {
            _cellStart[c] = offset;
            offset += _cellCount[c];
        }

        Array.Clear(_cellCount, 0, totalCells);

        for (var i = 0; i < state.UnitCount; i++)
        {
            var cell = _unitCells[i];
            if (cell < 0)
            {
                continue;
            }

            var index = _cellStart[cell] + _cellCount[cell];
            _cellUnits[index] = i;
            _cellCount[cell]++;
        }
    }

    public int FindNearestEnemy(
        BattleStateRuntime state,
        int unitIndex,
        Fix64 queryRange)
    {
        var unitPos = state.Positions[unitIndex];
        var team = state.Teams[unitIndex];
        var radiusSq = queryRange * queryRange;
        var cellRadius = Math.Max(1, (queryRange / _cellSize).CeilToInt());
        var (centerX, centerY) = GetCellCoords(unitPos.X, unitPos.Y);

        var nearest = -1;
        var nearestSq = radiusSq + Fix64.One;

        var minX = Math.Max(0, centerX - cellRadius);
        var maxX = Math.Min(_gridWidth - 1, centerX + cellRadius);
        var minY = Math.Max(0, centerY - cellRadius);
        var maxY = Math.Min(_gridHeight - 1, centerY + cellRadius);

        for (var y = minY; y <= maxY; y++)
        {
            var rowOffset = y * _gridWidth;
            for (var x = minX; x <= maxX; x++)
            {
                var cell = rowOffset + x;
                var start = _cellStart[cell];
                var count = _cellCount[cell];
                for (var i = 0; i < count; i++)
                {
                    var otherIndex = _cellUnits[start + i];
                    if (otherIndex == unitIndex)
                    {
                        continue;
                    }

                    if (state.States[otherIndex] == SimConstants.UnitDead || state.Teams[otherIndex] == team)
                    {
                        continue;
                    }

                    var dx = state.Positions[otherIndex].X - unitPos.X;
                    var dy = state.Positions[otherIndex].Y - unitPos.Y;
                    var distSq = (dx * dx) + (dy * dy);
                    if (distSq > radiusSq || distSq <= Fix64.Zero)
                    {
                        continue;
                    }

                    if (distSq < nearestSq || (distSq == nearestSq && otherIndex < nearest))
                    {
                        nearestSq = distSq;
                        nearest = otherIndex;
                    }
                }
            }
        }

        return nearest;
    }

    public void ForEachNeighbor(
        BattleStateRuntime state,
        int unitIndex,
        Fix64 radius,
        Action<int, FixVec2, Fix64> callback)
    {
        var unitPos = state.Positions[unitIndex];
        var radiusSq = radius * radius;
        var cellRadius = Math.Max(1, (radius / _cellSize).CeilToInt());
        var (centerX, centerY) = GetCellCoords(unitPos.X, unitPos.Y);

        var minX = Math.Max(0, centerX - cellRadius);
        var maxX = Math.Min(_gridWidth - 1, centerX + cellRadius);
        var minY = Math.Max(0, centerY - cellRadius);
        var maxY = Math.Min(_gridHeight - 1, centerY + cellRadius);

        for (var y = minY; y <= maxY; y++)
        {
            var rowOffset = y * _gridWidth;
            for (var x = minX; x <= maxX; x++)
            {
                var cell = rowOffset + x;
                var start = _cellStart[cell];
                var count = _cellCount[cell];
                for (var i = 0; i < count; i++)
                {
                    var otherIndex = _cellUnits[start + i];
                    if (otherIndex == unitIndex || state.States[otherIndex] == SimConstants.UnitDead)
                    {
                        continue;
                    }

                    var delta = state.Positions[otherIndex] - unitPos;
                    var distSq = delta.SqrMagnitude;
                    if (distSq > radiusSq || distSq <= Fix64.Zero)
                    {
                        continue;
                    }

                    callback(otherIndex, delta, distSq);
                }
            }
        }
    }

    private int GetCellIndex(Fix64 x, Fix64 y)
    {
        var cx = ((x - _originX) * _invCellSize).FloorToInt();
        var cy = ((y - _originY) * _invCellSize).FloorToInt();
        if (cx < 0 || cx >= _gridWidth || cy < 0 || cy >= _gridHeight)
        {
            return -1;
        }

        return (cy * _gridWidth) + cx;
    }

    private (int X, int Y) GetCellCoords(Fix64 x, Fix64 y)
    {
        var cx = ((x - _originX) * _invCellSize).FloorToInt();
        var cy = ((y - _originY) * _invCellSize).FloorToInt();
        return (cx, cy);
    }
}
