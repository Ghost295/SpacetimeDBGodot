#if TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace SpacetimeDB.Addons.FlowFieldBaker;

public static class FlowFieldBakeCSharpExporter
{
    private const string DefaultGeneratedFileName = "BakedFlowFieldGeneratedData.generated.cs";
    private const string GeneratedClassName = "BakedFlowFieldGeneratedData";
    private const byte PathableFlowFlag = 1 << 0;
    private const int DefaultGridCellsX = 5;
    private const int DefaultGridCellsZ = 3;
    private const int DefaultGridWorldSizeX = 120;
    private const int DefaultGridWorldSizeZ = 96;

    private readonly record struct CardGridMarkerData(
        byte Team,
        string GridId,
        int CenterX,
        int CenterZ);

    private readonly record struct BuildingObstacleData(
        int CenterX,
        int CenterZ,
        int Radius);

    private readonly record struct ExportMetadata(
        byte[] CardGridTeams,
        string[] CardGridIds,
        int[] CardGridCenterX,
        int[] CardGridCenterZ,
        int[] CardGridCellsX,
        int[] CardGridCellsZ,
        int[] CardGridWorldSizeX,
        int[] CardGridWorldSizeZ,
        int[] BuildingCenterX,
        int[] BuildingCenterZ,
        int[] BuildingRadius);

    public static bool TryExport(
        FlowFieldBakeNode bakeNode,
        string outputPath,
        out string absoluteOutputPath,
        out string error)
    {
        absoluteOutputPath = string.Empty;
        error = string.Empty;

        if (bakeNode == null)
        {
            error = "FlowFieldBakeNode is null.";
            return false;
        }

        if (bakeNode.BakedData == null)
        {
            error = "BakedData is not assigned.";
            return false;
        }

        if (!FlowFieldBakeHashUtility.TryDecode(
                bakeNode.BakedData,
                out FlowFieldBakeHashUtility.DecodedPayload payload,
                out error))
        {
            return false;
        }

        if (!TryValidateDecodedPayload(payload, out error))
        {
            return false;
        }

        if (!TryBuildExportMetadata(bakeNode, out var metadata, out error))
        {
            return false;
        }

        var hashBase64 = FlowFieldBakeHashUtility.ComputeHashBase64(payload);

        if (!TryResolveOutputPath(outputPath, out absoluteOutputPath, out error))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(absoluteOutputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            error = $"Could not resolve export directory from '{absoluteOutputPath}'.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var source = BuildGeneratedSource(payload, metadata, hashBase64);
            File.WriteAllText(absoluteOutputPath, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to export C# flowfield data: {ex.Message}";
            return false;
        }
    }

    private static bool TryValidateDecodedPayload(
        FlowFieldBakeHashUtility.DecodedPayload payload,
        out string error)
    {
        error = string.Empty;

        if (payload.CellSize <= 0 || payload.Width <= 0 || payload.Height <= 0)
        {
            error = $"Invalid flowfield dimensions. CellSize={payload.CellSize}, Width={payload.Width}, Height={payload.Height}.";
            return false;
        }

        if (payload.Height > int.MaxValue / payload.Width)
        {
            error = $"Flowfield dimensions are too large: {payload.Width}x{payload.Height}.";
            return false;
        }

        var expectedLength = payload.Width * payload.Height;
        if (payload.CostBytes.Length != expectedLength ||
            payload.Team0FlowBytes.Length != expectedLength ||
            payload.Team1FlowBytes.Length != expectedLength)
        {
            error = "Decoded flowfield byte lengths do not match Width * Height.";
            return false;
        }

        var team0PathableCount = CountPathableFlowCells(payload.Team0FlowBytes);
        var team1PathableCount = CountPathableFlowCells(payload.Team1FlowBytes);
        if (team0PathableCount <= 0 || team1PathableCount <= 0)
        {
            error =
                $"Flowfield is missing pathable flow cells (Team0={team0PathableCount}, Team1={team1PathableCount}). " +
                "Re-bake with valid goals on walkable cells.";
            return false;
        }

        return true;
    }

    private static int CountPathableFlowCells(byte[] flowBytes)
    {
        var count = 0;
        for (var i = 0; i < flowBytes.Length; i++)
        {
            var flags = (byte)((flowBytes[i] >> 4) & 0x0F);
            if ((flags & PathableFlowFlag) != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static bool TryBuildExportMetadata(
        FlowFieldBakeNode bakeNode,
        out ExportMetadata metadata,
        out string error)
    {
        metadata = default;
        error = string.Empty;

        var markers = CollectCardGridMarkers(bakeNode);
        if (markers.Count == 0)
        {
            error =
                "No card placement grids were found in the current scene. " +
                "Add CardPlacementGrid markers before exporting.";
            return false;
        }

        var hasTeam0 = false;
        var hasTeam1 = false;
        for (var i = 0; i < markers.Count; i++)
        {
            if (markers[i].Team == 0)
            {
                hasTeam0 = true;
            }
            else if (markers[i].Team == 1)
            {
                hasTeam1 = true;
            }
        }

        if (!hasTeam0 || !hasTeam1)
        {
            error =
                $"Expected both teams to have at least one grid marker before export (team0={hasTeam0}, team1={hasTeam1}).";
            return false;
        }

        var cardGridCount = markers.Count;
        var teams = new byte[cardGridCount];
        var ids = new string[cardGridCount];
        var centerX = new int[cardGridCount];
        var centerZ = new int[cardGridCount];
        var cellsX = new int[cardGridCount];
        var cellsZ = new int[cardGridCount];
        var worldSizeX = new int[cardGridCount];
        var worldSizeZ = new int[cardGridCount];
        var idCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < cardGridCount; i++)
        {
            var marker = markers[i];
            teams[i] = marker.Team;
            centerX[i] = marker.CenterX;
            centerZ[i] = marker.CenterZ;
            cellsX[i] = DefaultGridCellsX;
            cellsZ[i] = DefaultGridCellsZ;
            worldSizeX[i] = DefaultGridWorldSizeX;
            worldSizeZ[i] = DefaultGridWorldSizeZ;

            var id = string.IsNullOrWhiteSpace(marker.GridId)
                ? $"grid_{marker.Team}_{i}"
                : marker.GridId.Trim();
            if (!idCounts.TryGetValue(id, out var seen))
            {
                idCounts[id] = 1;
                ids[i] = id;
            }
            else
            {
                seen++;
                idCounts[id] = seen;
                ids[i] = $"{id}_{seen}";
            }
        }

        var buildingObstacles = CollectBuildingObstacles(bakeNode.ResolveBuildingContainerNode());
        var buildingCount = buildingObstacles.Count;
        var buildingCenterX = new int[buildingCount];
        var buildingCenterZ = new int[buildingCount];
        var buildingRadius = new int[buildingCount];
        for (var i = 0; i < buildingCount; i++)
        {
            var obstacle = buildingObstacles[i];
            buildingCenterX[i] = obstacle.CenterX;
            buildingCenterZ[i] = obstacle.CenterZ;
            buildingRadius[i] = obstacle.Radius;
        }

        metadata = new ExportMetadata(
            teams,
            ids,
            centerX,
            centerZ,
            cellsX,
            cellsZ,
            worldSizeX,
            worldSizeZ,
            buildingCenterX,
            buildingCenterZ,
            buildingRadius);
        return true;
    }

    private static List<CardGridMarkerData> CollectCardGridMarkers(FlowFieldBakeNode bakeNode)
    {
        var markers = new List<CardGridMarkerData>();
        if (bakeNode == null)
        {
            return markers;
        }

        var root = bakeNode.GetParent() ?? bakeNode;
        var visited = new HashSet<ulong>();
        CollectCardGridMarkersRecursive(root, visited, markers);
        markers.Sort(static (a, b) =>
        {
            var teamCmp = a.Team.CompareTo(b.Team);
            if (teamCmp != 0)
            {
                return teamCmp;
            }

            var zCmp = a.CenterZ.CompareTo(b.CenterZ);
            if (zCmp != 0)
            {
                return zCmp;
            }

            var xCmp = a.CenterX.CompareTo(b.CenterX);
            if (xCmp != 0)
            {
                return xCmp;
            }

            return string.CompareOrdinal(a.GridId, b.GridId);
        });
        return markers;
    }

    private static void CollectCardGridMarkersRecursive(
        Node node,
        HashSet<ulong> visited,
        List<CardGridMarkerData> output)
    {
        if (node == null)
        {
            return;
        }

        if (node is Node3D node3D &&
            IsCardGridCandidate(node3D) &&
            TryBuildCardGridMarker(node3D, out var marker))
        {
            var instanceId = node3D.GetInstanceId();
            if (visited.Add(instanceId))
            {
                output.Add(marker);
            }
        }

        var children = node.GetChildren();
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is Node child)
            {
                CollectCardGridMarkersRecursive(child, visited, output);
            }
        }
    }

    private static bool IsCardGridCandidate(Node3D node)
    {
        if (node is CardPlacementGridMarker3D)
        {
            return true;
        }

        var name = node.Name.ToString();
        return node.IsInGroup("CardPlacementGrid") ||
               name.Contains("CardGrid", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryBuildCardGridMarker(Node3D node, out CardGridMarkerData marker)
    {
        marker = default;
        if (node == null)
        {
            return false;
        }

        var team = (byte)0;
        var gridId = string.Empty;
        var name = node.Name.ToString();
        if (node is CardPlacementGridMarker3D typed)
        {
            team = typed.Team;
            gridId = typed.GridId ?? string.Empty;
        }
        else if (node.IsInGroup("CardPlacementGrid_Team1") ||
                 name.Contains("Team1", StringComparison.OrdinalIgnoreCase))
        {
            team = 1;
        }

        if (string.IsNullOrWhiteSpace(gridId))
        {
            gridId = name;
        }

        marker = new CardGridMarkerData(
            team,
            gridId.Trim(),
            Mathf.RoundToInt(node.GlobalPosition.X),
            Mathf.RoundToInt(node.GlobalPosition.Z));
        return true;
    }

    private static List<BuildingObstacleData> CollectBuildingObstacles(Node buildingContainer)
    {
        var result = new List<BuildingObstacleData>();
        if (buildingContainer == null)
        {
            return result;
        }

        var extracted = new List<(int CenterX, int CenterZ, int Radius, string Key)>();
        var children = buildingContainer.GetChildren();
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is not Node3D building)
            {
                continue;
            }

            if (!TryExtractWorldFootprint(building, out var worldMin, out var worldMax))
            {
                continue;
            }

            var centerX = Mathf.RoundToInt((worldMin.X + worldMax.X) * 0.5f);
            var centerZ = Mathf.RoundToInt((worldMin.Z + worldMax.Z) * 0.5f);
            var radius = Mathf.RoundToInt(
                Mathf.Max(0f, Mathf.Max((worldMax.X - worldMin.X) * 0.5f, (worldMax.Z - worldMin.Z) * 0.5f)));
            extracted.Add((centerX, centerZ, radius, building.GetPath().ToString()));
        }

        extracted.Sort(static (a, b) =>
        {
            var zCmp = a.CenterZ.CompareTo(b.CenterZ);
            if (zCmp != 0)
            {
                return zCmp;
            }

            var xCmp = a.CenterX.CompareTo(b.CenterX);
            if (xCmp != 0)
            {
                return xCmp;
            }

            var rCmp = a.Radius.CompareTo(b.Radius);
            if (rCmp != 0)
            {
                return rCmp;
            }

            return string.CompareOrdinal(a.Key, b.Key);
        });

        for (var i = 0; i < extracted.Count; i++)
        {
            var obstacle = extracted[i];
            result.Add(new BuildingObstacleData(obstacle.CenterX, obstacle.CenterZ, obstacle.Radius));
        }

        return result;
    }

    private static bool TryExtractWorldFootprint(Node3D building, out Vector3 worldMin, out Vector3 worldMax)
    {
        worldMin = default;
        worldMax = default;
        var collisionShapes = new List<CollisionShape3D>();
        CollectCollisionShapesRecursive(building, collisionShapes);
        if (collisionShapes.Count <= 0)
        {
            return false;
        }

        CollisionShape3D selectedShape = null;
        string selectedKey = null;
        for (var i = 0; i < collisionShapes.Count; i++)
        {
            var shapeNode = collisionShapes[i];
            if (shapeNode?.Shape == null)
            {
                continue;
            }

            var key = shapeNode.GetPath().ToString();
            if (selectedShape == null || string.CompareOrdinal(key, selectedKey) < 0)
            {
                selectedShape = shapeNode;
                selectedKey = key;
            }
        }

        if (selectedShape?.Shape == null || !TryGetLocalAabb(selectedShape.Shape, out var selectedAabb))
        {
            return false;
        }

        GetWorldMinMax(selectedShape.GlobalTransform, selectedAabb, out worldMin, out worldMax);
        return true;
    }

    private static void CollectCollisionShapesRecursive(Node node, List<CollisionShape3D> output)
    {
        if (node == null || output == null)
        {
            return;
        }

        var children = node.GetChildren();
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is not Node child)
            {
                continue;
            }

            if (child is CollisionShape3D shape)
            {
                output.Add(shape);
            }

            CollectCollisionShapesRecursive(child, output);
        }
    }

    private static bool TryGetLocalAabb(Shape3D shape, out Aabb localAabb)
    {
        switch (shape)
        {
            case BoxShape3D box:
                localAabb = new Aabb(-box.Size * 0.5f, box.Size);
                return true;
            case SphereShape3D sphere:
            {
                var radius = sphere.Radius;
                localAabb = new Aabb(new Vector3(-radius, -radius, -radius), new Vector3(2f * radius, 2f * radius, 2f * radius));
                return true;
            }
            case CylinderShape3D cylinder:
            {
                var radius = cylinder.Radius;
                var height = cylinder.Height;
                localAabb = new Aabb(new Vector3(-radius, -height * 0.5f, -radius), new Vector3(2f * radius, height, 2f * radius));
                return true;
            }
            case CapsuleShape3D capsule:
            {
                var radius = capsule.Radius;
                var height = capsule.Height + (2f * radius);
                localAabb = new Aabb(new Vector3(-radius, -height * 0.5f, -radius), new Vector3(2f * radius, height, 2f * radius));
                return true;
            }
            default:
                localAabb = default;
                return false;
        }
    }

    private static void GetWorldMinMax(Transform3D transform, Aabb localAabb, out Vector3 worldMin, out Vector3 worldMax)
    {
        var p = localAabb.Position;
        var s = localAabb.Size;
        Span<Vector3> corners = stackalloc Vector3[8];
        corners[0] = p;
        corners[1] = p + new Vector3(s.X, 0, 0);
        corners[2] = p + new Vector3(0, s.Y, 0);
        corners[3] = p + new Vector3(0, 0, s.Z);
        corners[4] = p + new Vector3(s.X, s.Y, 0);
        corners[5] = p + new Vector3(s.X, 0, s.Z);
        corners[6] = p + new Vector3(0, s.Y, s.Z);
        corners[7] = p + s;

        var min = transform * corners[0];
        var max = min;
        for (var i = 1; i < corners.Length; i++)
        {
            var world = transform * corners[i];
            min.X = Mathf.Min(min.X, world.X);
            min.Y = Mathf.Min(min.Y, world.Y);
            min.Z = Mathf.Min(min.Z, world.Z);
            max.X = Mathf.Max(max.X, world.X);
            max.Y = Mathf.Max(max.Y, world.Y);
            max.Z = Mathf.Max(max.Z, world.Z);
        }

        worldMin = min;
        worldMax = max;
    }

    private static bool TryResolveOutputPath(string outputPath, out string absolutePath, out string error)
    {
        absolutePath = string.Empty;
        error = string.Empty;

        var trimmed = (outputPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = "Export path is empty.";
            return false;
        }

        try
        {
            var normalized = trimmed.Replace('\\', '/');
            var inputLooksLikeFilePath = normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
            if (normalized.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
            {
                absolutePath = ProjectSettings.GlobalizePath(normalized);
            }
            else if (Path.IsPathRooted(trimmed))
            {
                absolutePath = Path.GetFullPath(trimmed);
            }
            else
            {
                var projectRoot = ProjectSettings.GlobalizePath("res://");
                absolutePath = Path.GetFullPath(trimmed, projectRoot);
            }

            if (!inputLooksLikeFilePath)
            {
                absolutePath = Path.Combine(absolutePath, DefaultGeneratedFileName);
            }

            if (!absolutePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                error = $"Resolved export file must end with .cs. Got '{absolutePath}'.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Invalid export path '{trimmed}': {ex.Message}";
            return false;
        }
    }

    private static string BuildGeneratedSource(
        FlowFieldBakeHashUtility.DecodedPayload payload,
        ExportMetadata metadata,
        string hashBase64)
    {
        var builder = new StringBuilder(4096);
        builder.AppendLine("using System;");
        builder.AppendLine();
        builder.AppendLine("// <auto-generated>");
        builder.AppendLine("// Generated by FlowFieldBakeCSharpExporter in the Godot editor.");
        builder.AppendLine("// </auto-generated>");
        builder.AppendLine($"internal static class {GeneratedClassName}");
        builder.AppendLine("{");
        builder.AppendLine($"    public const string BakeHashBase64 = \"{hashBase64}\";");
        builder.AppendLine($"    public const int Version = {payload.Version};");
        builder.AppendLine($"    public const int CellSize = {payload.CellSize};");
        builder.AppendLine($"    public const int Width = {payload.Width};");
        builder.AppendLine($"    public const int Height = {payload.Height};");
        builder.AppendLine($"    public const int FieldSizeX = {payload.FieldSize.X};");
        builder.AppendLine($"    public const int FieldSizeY = {payload.FieldSize.Y};");
        builder.AppendLine($"    public const int WorldOriginX = {payload.WorldOrigin.X};");
        builder.AppendLine($"    public const int WorldOriginY = {payload.WorldOrigin.Y};");
        builder.AppendLine($"    public const int WorldOriginZ = {payload.WorldOrigin.Z};");
        builder.AppendLine($"    public const int Team0GoalCellX = {payload.Team0GoalCell.X};");
        builder.AppendLine($"    public const int Team0GoalCellY = {payload.Team0GoalCell.Y};");
        builder.AppendLine($"    public const int Team1GoalCellX = {payload.Team1GoalCell.X};");
        builder.AppendLine($"    public const int Team1GoalCellY = {payload.Team1GoalCell.Y};");
        builder.AppendLine();
        AppendByteArrayField(builder, "CardGridTeams", metadata.CardGridTeams);
        AppendStringArrayField(builder, "CardGridIds", metadata.CardGridIds);
        AppendIntArrayField(builder, "CardGridCenterX", metadata.CardGridCenterX);
        AppendIntArrayField(builder, "CardGridCenterZ", metadata.CardGridCenterZ);
        AppendIntArrayField(builder, "CardGridCellsX", metadata.CardGridCellsX);
        AppendIntArrayField(builder, "CardGridCellsZ", metadata.CardGridCellsZ);
        AppendIntArrayField(builder, "CardGridWorldSizeX", metadata.CardGridWorldSizeX);
        AppendIntArrayField(builder, "CardGridWorldSizeZ", metadata.CardGridWorldSizeZ);
        AppendIntArrayField(builder, "BuildingCenterX", metadata.BuildingCenterX);
        AppendIntArrayField(builder, "BuildingCenterZ", metadata.BuildingCenterZ);
        AppendIntArrayField(builder, "BuildingRadius", metadata.BuildingRadius);
        builder.AppendLine("    public static readonly int[] BuildingBlockedMinX = Array.Empty<int>();");
        builder.AppendLine("    public static readonly int[] BuildingBlockedMinY = Array.Empty<int>();");
        builder.AppendLine("    public static readonly int[] BuildingBlockedMaxX = Array.Empty<int>();");
        builder.AppendLine("    public static readonly int[] BuildingBlockedMaxY = Array.Empty<int>();");
        builder.AppendLine();
        builder.AppendLine("    public static readonly byte[] CostBytes;");
        builder.AppendLine("    public static readonly byte[] Team0FlowBytes;");
        builder.AppendLine("    public static readonly byte[] Team1FlowBytes;");
        builder.AppendLine();
        builder.AppendLine($"    static {GeneratedClassName}()");
        builder.AppendLine("    {");
        AppendBase64DecodeAssignment(builder, "CostBytes", payload.CostBytes);
        AppendBase64DecodeAssignment(builder, "Team0FlowBytes", payload.Team0FlowBytes);
        AppendBase64DecodeAssignment(builder, "Team1FlowBytes", payload.Team1FlowBytes);
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static byte[] DecodeBase64(string base64)");
        builder.AppendLine("    {");
        builder.AppendLine("        return string.IsNullOrEmpty(base64) ? Array.Empty<byte>() : Convert.FromBase64String(base64);");
        builder.AppendLine("    }");
        builder.AppendLine("}");

        return builder.ToString();
    }

    private static void AppendBase64DecodeAssignment(StringBuilder builder, string fieldName, byte[] bytes)
    {
        var base64 = Convert.ToBase64String(bytes);
        builder.AppendLine($"        {fieldName} = DecodeBase64(");
        AppendWrappedStringLiteral(builder, base64, "            ");
        builder.AppendLine("        );");
    }

    private static void AppendByteArrayField(StringBuilder builder, string fieldName, byte[] values)
    {
        if (values == null || values.Length == 0)
        {
            builder.AppendLine($"    public static readonly byte[] {fieldName} = Array.Empty<byte>();");
            return;
        }

        builder.Append($"    public static readonly byte[] {fieldName} = new byte[] {{ ");
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");
            builder.Append(values[i]);
        }

        builder.AppendLine(" };");
    }

    private static void AppendIntArrayField(StringBuilder builder, string fieldName, int[] values)
    {
        if (values == null || values.Length == 0)
        {
            builder.AppendLine($"    public static readonly int[] {fieldName} = Array.Empty<int>();");
            return;
        }

        builder.Append($"    public static readonly int[] {fieldName} = new int[] {{ ");
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");
            builder.Append(values[i]);
        }

        builder.AppendLine(" };");
    }

    private static void AppendStringArrayField(StringBuilder builder, string fieldName, string[] values)
    {
        if (values == null || values.Length == 0)
        {
            builder.AppendLine($"    public static readonly string[] {fieldName} = Array.Empty<string>();");
            return;
        }

        builder.Append($"    public static readonly string[] {fieldName} = new string[] {{ ");
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");
            builder.Append('"');
            builder.Append((values[i] ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\""));
            builder.Append('"');
        }

        builder.AppendLine(" };");
    }

    private static void AppendWrappedStringLiteral(StringBuilder builder, string value, string indent)
    {
        if (value.Length == 0)
        {
            builder.Append(indent).AppendLine("\"\"");
            return;
        }

        const int chunkSize = 120;
        for (var offset = 0; offset < value.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, value.Length - offset);
            var chunk = value.Substring(offset, length);
            var isLast = offset + length >= value.Length;
            builder.Append(indent).Append('"').Append(chunk).Append('"');
            if (!isLast)
            {
                builder.Append(" +");
            }

            builder.AppendLine();
        }
    }
}
#endif
