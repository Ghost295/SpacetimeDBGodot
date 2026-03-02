#if TOOLS
using System;
using System.IO;
using System.Text;
using Godot;

namespace SpacetimeDB.Addons.FlowFieldBaker;

public static class FlowFieldBakeCSharpExporter
{
    private const string DefaultGeneratedFileName = "BakedFlowFieldGeneratedData.generated.cs";
    private const string GeneratedClassName = "BakedFlowFieldGeneratedData";
    private const byte PathableFlowFlag = 1 << 0;

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

        if (!FlowFieldBakeHashUtility.TryComputeHashBase64(bakeNode.BakedData, out var hashBase64, out error))
        {
            return false;
        }

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
            var source = BuildGeneratedSource(payload, hashBase64);
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

    private static string BuildGeneratedSource(FlowFieldBakeHashUtility.DecodedPayload payload, string hashBase64)
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
