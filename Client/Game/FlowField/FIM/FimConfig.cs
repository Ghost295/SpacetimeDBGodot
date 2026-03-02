using System;
using Godot;

namespace SpacetimeDB.Game.FlowField.FIM;

public sealed class FimConfig
{
    public int ItersPerSweep { get; init; } = 10;
    public float Epsilon { get; init; } = 1e-5f;
    public float StepScale { get; init; } = 1f;
    public float MinimumSlowness { get; init; } = 1f;
    public float InfinityValue { get; init; } = 1e9f;
    public bool UseDiagonalStencil { get; init; } = true;
    public int TileSize { get; init; } = 64;
    public int NavTileSize { get; init; } = 128;
    public int RegionSize { get; init; } = 1024;
    public bool EnableTiledProcessing { get; init; } = true;
    public int ParallelTileThreshold { get; init; } = 1;
    public int NeighborItersPerSweep { get; init; } = 1;
    public bool EnqueueDiagonalNeighborTiles { get; init; } = false;
    public int? MaxDegreeOfParallelism { get; init; }
    public bool AllowParallel { get; init; } = true;
    public int CellSize { get; init; } = 1;
    public Vector2I FieldSize { get; init; } = Vector2I.Zero;
    public Vector3I WorldOrigin { get; init; } = Vector3I.Zero;

    public void Validate()
    {
        if (ItersPerSweep <= 0) throw new ArgumentOutOfRangeException(nameof(ItersPerSweep));
        if (Epsilon <= 0f) throw new ArgumentOutOfRangeException(nameof(Epsilon));
        if (StepScale <= 0f) throw new ArgumentOutOfRangeException(nameof(StepScale));
        if (MinimumSlowness <= 0f) throw new ArgumentOutOfRangeException(nameof(MinimumSlowness));
        if (InfinityValue <= 0f) throw new ArgumentOutOfRangeException(nameof(InfinityValue));
        if (TileSize <= 0) throw new ArgumentOutOfRangeException(nameof(TileSize));
        if (ParallelTileThreshold < 0) throw new ArgumentOutOfRangeException(nameof(ParallelTileThreshold));
        if (NeighborItersPerSweep <= 0) throw new ArgumentOutOfRangeException(nameof(NeighborItersPerSweep));
        if (CellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(CellSize));
        if (MaxDegreeOfParallelism is { } max && max <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDegreeOfParallelism));
        }
    }
}


