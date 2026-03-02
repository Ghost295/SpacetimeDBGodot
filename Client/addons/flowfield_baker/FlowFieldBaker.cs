using System;
using SpacetimeDB.Game.FlowField;
using Godot;
using SpacetimeDB.Game.FlowField.FIM;

namespace SpacetimeDB.Addons.FlowFieldBaker;

public static class FlowFieldBaker
{
    public static bool TryBakeAndSave(FlowFieldBakeNode bakeNode, out string outputPath, out string error)
    {
        outputPath = string.Empty;
        error = string.Empty;

        if (bakeNode == null)
        {
            error = "FlowFieldBakeNode is null.";
            return false;
        }

        if (!bakeNode.EnsureBakedData(out error))
            return false;
        if (!bakeNode.TryCreateFimConfig(out FimConfig config, out error))
            return false;

        Node terrainNode = bakeNode.ResolveTerrainNode();
        if (terrainNode == null)
        {
            error = "Terrain3DPath is not set or invalid.";
            return false;
        }

        int width = config.FieldSize.X / config.CellSize;
        int height = config.FieldSize.Y / config.CellSize;

        if (!TryBuildTerrainCosts(terrainNode, config, out byte[] costs, out error))
            return false;

        ApplyPaintedCosts(bakeNode.BakedData, costs);
        // ApplyBuildingBlockers(bakeNode.ResolveBuildingContainerNode(), config, costs);

        int pathableCells = CountPathableCells(costs);
        if (pathableCells <= 0)
        {
            error = "Costfield has no pathable cells after terrain + paint + building blockers. Bake cannot continue.";
            return false;
        }

        var costField = new CostField(width, height, costs);

        if (!bakeNode.TryGetGoalCells(config, out Vector2I team0GoalCell, out Vector2I team1GoalCell, out error))
            return false;
        if (!IsGoalPathable(costs, width, team0GoalCell))
        {
            error = $"Team0 goal cell {team0GoalCell} is blocked in the baked costfield. Move the goal marker to a walkable location.";
            return false;
        }
        if (!IsGoalPathable(costs, width, team1GoalCell))
        {
            error = $"Team1 goal cell {team1GoalCell} is blocked in the baked costfield. Move the goal marker to a walkable location.";
            return false;
        }

        var solver = new EikonalCpuSolver();
        var team0Integration = solver.Solve(
            costField,
            new[] { new EikonalCpuSolver.Seed(team0GoalCell.X, team0GoalCell.Y, 0f) },
            config,
            allowedTiles: null);
        var team1Integration = solver.Solve(
            costField,
            new[] { new EikonalCpuSolver.Seed(team1GoalCell.X, team1GoalCell.Y, 0f) },
            config,
            allowedTiles: null);

        FlowField team0Flow = FlowFieldBuilder.BuildFlowField(team0Integration, costField, config, allowedTiles: null);
        FlowField team1Flow = FlowFieldBuilder.BuildFlowField(team1Integration, costField, config, allowedTiles: null);
        if (!ValidateFlowHasPathableCells(team0Flow.DirectionsAndFlags, "Team0", out error))
            return false;
        if (!ValidateFlowHasPathableCells(team1Flow.DirectionsAndFlags, "Team1", out error))
            return false;

        MapFlowFieldData data = bakeNode.BakedData;
        if (data == null)
        {
            error = "BakedData is not assigned.";
            return false;
        }
        data.Version = Math.Max(1, data.Version);
        data.CellSize = config.CellSize;
        data.FieldSize = config.FieldSize;
        data.WorldOrigin = config.WorldOrigin;
        data.Team0GoalCell = team0GoalCell;
        data.Team1GoalCell = team1GoalCell;
        data.CostfieldR8 = Image.CreateFromData(width, height, false, Image.Format.R8, (byte[])costField.Costs.Clone());
        data.FlowTeam0R8 = Image.CreateFromData(width, height, false, Image.Format.R8, (byte[])team0Flow.DirectionsAndFlags.Clone());
        data.FlowTeam1R8 = Image.CreateFromData(width, height, false, Image.Format.R8, (byte[])team1Flow.DirectionsAndFlags.Clone());

        if (!FlowFieldBakeHashUtility.TryComputeAndStoreHash(data, out error))
            return false;

        if (!bakeNode.TryGetBakedDataResourcePath(out outputPath, out error))
            return false;

        Error saveErr = ResourceSaver.Save(data, outputPath);
        if (saveErr != Error.Ok)
        {
            error = $"ResourceSaver.Save failed for '{outputPath}' with code {saveErr}.";
            return false;
        }

        bakeNode.BakedData = data;
        bakeNode.RefreshOverlayFromCostfield();

        GD.Print($"[FlowFieldBaker] Baked and saved flowfields -> {outputPath}");
        GD.Print($"[FlowFieldBaker] Hash={data.BakeHashBase64}");
        return true;
    }

    private static int CountPathableCells(byte[] costs)
    {
        int count = 0;
        for (int i = 0; i < costs.Length; i++)
        {
            if (costs[i] != byte.MaxValue)
                count++;
        }
        return count;
    }

    private static bool IsGoalPathable(byte[] costs, int width, Vector2I goalCell)
    {
        int idx = goalCell.Y * width + goalCell.X;
        return (uint)idx < (uint)costs.Length && costs[idx] != byte.MaxValue;
    }

    private static bool ValidateFlowHasPathableCells(byte[] flowBytes, string teamLabel, out string error)
    {
        error = string.Empty;
        if (flowBytes == null || flowBytes.Length == 0)
        {
            error = $"{teamLabel} flow bytes are empty.";
            return false;
        }

        int pathableCount = 0;
        int nonZeroRawCount = 0;
        for (int i = 0; i < flowBytes.Length; i++)
        {
            byte raw = flowBytes[i];
            if (raw != 0)
                nonZeroRawCount++;

            var flags = (FlowFlags.FlowFieldFlags)((raw >> 4) & 0x0F);
            if ((flags & FlowFlags.FlowFieldFlags.Pathable) != 0)
                pathableCount++;
        }

        if (pathableCount <= 0)
        {
            error =
                $"{teamLabel} flowfield contains zero pathable cells (nonZeroRaw={nonZeroRawCount}). " +
                "This usually means the goal seed was baked on a blocked cell.";
            return false;
        }

        return true;
    }

    private static bool TryBuildTerrainCosts(Node terrainNode, FimConfig config, out byte[] costs, out string error)
    {
        costs = Array.Empty<byte>();
        error = string.Empty;

        GodotObject terrainData = terrainNode.Get("data").AsGodotObject();
        if (terrainData == null)
        {
            error = "Terrain3D node has no 'data' object.";
            return false;
        }

        int width = config.FieldSize.X / config.CellSize;
        int height = config.FieldSize.Y / config.CellSize;
        costs = new byte[width * height];

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                float worldX = config.WorldOrigin.X + (x + 0.5f) * config.CellSize;
                float worldZ = config.WorldOrigin.Z + (y + 0.5f) * config.CellSize;
                bool walkable = terrainData.Call("get_control_navigation", new Vector3(worldX, 0f, worldZ)).AsBool();
                costs[row + x] = walkable ? (byte)1 : byte.MaxValue;
            }
        }

        return true;
    }

    private static void ApplyPaintedCosts(MapFlowFieldData paintedData, byte[] costs)
    {
        if (paintedData == null || paintedData.CostfieldR8 == null)
            return;

        Image paintedImage = paintedData.CostfieldR8;
        if (paintedImage.GetFormat() != Image.Format.R8)
        {
            paintedImage = paintedImage.Duplicate() as Image;
            if (paintedImage == null)
                return;
            paintedImage.Convert(Image.Format.R8);
        }

        byte[] paintedBytes = paintedImage.GetData();

        int count = Math.Min(costs.Length, paintedBytes.Length);
        for (int i = 0; i < count; i++)
        {
            // Terrain/nav hard block always wins.
            if (costs[i] == byte.MaxValue)
                continue;

            byte paintedCost = paintedBytes[i];
            if (paintedCost == byte.MaxValue)
            {
                costs[i] = byte.MaxValue;
                continue;
            }

            if (paintedCost <= 0)
                paintedCost = 1;

            costs[i] = paintedCost;
        }
    }
}
