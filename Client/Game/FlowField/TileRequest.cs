using SpacetimeDB.Game.FlowField.FIM;
using System.Collections.Generic;

namespace SpacetimeDB.Game.FlowField;

public readonly record struct TileRequest(
    CostField Cost,
    IReadOnlyList<EikonalCpuSolver.Seed> Seeds,
    bool[]? LineOfSightMask,
    FimConfig Config);

