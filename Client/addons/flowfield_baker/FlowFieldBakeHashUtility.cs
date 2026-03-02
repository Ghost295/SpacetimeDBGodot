using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Godot;

namespace SpacetimeDB.Addons.FlowFieldBaker;

public static class FlowFieldBakeHashUtility
{
    public readonly record struct DecodedPayload(
        int Version,
        int CellSize,
        Vector2I FieldSize,
        Vector3I WorldOrigin,
        Vector2I Team0GoalCell,
        Vector2I Team1GoalCell,
        int Width,
        int Height,
        byte[] CostBytes,
        byte[] Team0FlowBytes,
        byte[] Team1FlowBytes);

    public static bool TryDecode(MapFlowFieldData data, out DecodedPayload payload, out string error)
    {
        payload = default;
        error = string.Empty;

        if (data == null)
        {
            error = "MapFlowFieldData is null.";
            return false;
        }

        if (!data.TryGetGridSize(out int width, out int height))
        {
            error = $"Invalid map metadata. CellSize={data.CellSize}, FieldSize={data.FieldSize}.";
            return false;
        }

        if (!TryGetR8ImageBytes(data.CostfieldR8, width, height, "CostfieldR8", out byte[] costBytes, out error))
            return false;
        if (!TryGetR8ImageBytes(data.FlowTeam0R8, width, height, "FlowTeam0R8", out byte[] team0Bytes, out error))
            return false;
        if (!TryGetR8ImageBytes(data.FlowTeam1R8, width, height, "FlowTeam1R8", out byte[] team1Bytes, out error))
            return false;

        payload = new DecodedPayload(
            data.Version,
            data.CellSize,
            data.FieldSize,
            data.WorldOrigin,
            data.Team0GoalCell,
            data.Team1GoalCell,
            width,
            height,
            costBytes,
            team0Bytes,
            team1Bytes);

        return true;
    }

    public static bool TryComputeHashBase64(MapFlowFieldData data, out string hashBase64, out string error)
    {
        hashBase64 = string.Empty;
        error = string.Empty;

        if (!TryDecode(data, out DecodedPayload payload, out error))
            return false;

        hashBase64 = ComputeHashBase64(payload);
        return true;
    }

    public static string ComputeHashBase64(DecodedPayload payload, byte[] metadataExtensionBytes = null)
    {
        byte[] coreMetadataBytes = BuildCoreMetadataBytes(payload);
        byte[] hashBytes = ComputeSha256(
            coreMetadataBytes,
            metadataExtensionBytes ?? Array.Empty<byte>(),
            payload.CostBytes,
            payload.Team0FlowBytes,
            payload.Team1FlowBytes);
        return Convert.ToBase64String(hashBytes);
    }

    public static bool TryComputeAndStoreHash(MapFlowFieldData data, out string error)
    {
        error = string.Empty;
        if (!TryComputeHashBase64(data, out string hashBase64, out error))
            return false;

        data.BakeHashBase64 = hashBase64;
        return true;
    }

    private static byte[] BuildCoreMetadataBytes(DecodedPayload payload)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        writer.Write(payload.Version);
        writer.Write(payload.CellSize);
        writer.Write(payload.FieldSize.X);
        writer.Write(payload.FieldSize.Y);
        writer.Write(payload.WorldOrigin.X);
        writer.Write(payload.WorldOrigin.Y);
        writer.Write(payload.WorldOrigin.Z);
        writer.Write(payload.Team0GoalCell.X);
        writer.Write(payload.Team0GoalCell.Y);
        writer.Write(payload.Team1GoalCell.X);
        writer.Write(payload.Team1GoalCell.Y);
        writer.Write(payload.Width);
        writer.Write(payload.Height);

        writer.Flush();
        return ms.ToArray();
    }

    private static byte[] ComputeSha256(
        byte[] coreMetadataBytes,
        byte[] metadataExtensionBytes,
        byte[] costBytes,
        byte[] team0Bytes,
        byte[] team1Bytes)
    {
        using var sha = SHA256.Create();
        using var ms = new MemoryStream(
            coreMetadataBytes.Length + metadataExtensionBytes.Length + costBytes.Length + team0Bytes.Length + team1Bytes.Length + 16);
        ms.Write(coreMetadataBytes);
        ms.Write(metadataExtensionBytes);
        ms.Write(costBytes);
        ms.Write(team0Bytes);
        ms.Write(team1Bytes);
        return sha.ComputeHash(ms.ToArray());
    }

    private static bool TryGetR8ImageBytes(
        Image image,
        int expectedWidth,
        int expectedHeight,
        string imageLabel,
        out byte[] bytes,
        out string error)
    {
        bytes = Array.Empty<byte>();
        error = string.Empty;

        if (image == null)
        {
            error = $"{imageLabel} is null.";
            return false;
        }

        if (image.GetWidth() != expectedWidth || image.GetHeight() != expectedHeight)
        {
            error =
                $"{imageLabel} dimensions mismatch. Expected {expectedWidth}x{expectedHeight}, got {image.GetWidth()}x{image.GetHeight()}.";
            return false;
        }

        Image source = image;
        if (source.GetFormat() != Image.Format.R8)
        {
            source = source.Duplicate() as Image;
            if (source == null)
            {
                error = $"{imageLabel} could not be duplicated for R8 conversion.";
                return false;
            }

            source.Convert(Image.Format.R8);
        }

        bytes = source.GetData();
        int expectedLength = expectedWidth * expectedHeight;
        if (bytes.Length != expectedLength)
        {
            error = $"{imageLabel} byte length mismatch. Expected {expectedLength}, got {bytes.Length}.";
            return false;
        }

        return true;
    }

}
