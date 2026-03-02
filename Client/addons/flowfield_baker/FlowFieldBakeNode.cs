using System;
using SpacetimeDB.Game.FlowField;
using Godot;
using SpacetimeDB.Game.FlowField.FIM;

namespace SpacetimeDB.Addons.FlowFieldBaker;

[Tool, GlobalClass]
public partial class FlowFieldBakeNode : Node3D
{
    public enum BrushMode
    {
        Paint = 0,
        Erase = 1,
        Sample = 2
    }

    public enum FlowArrowTeamSelection
    {
        Team0 = 0,
        Team1 = 1
    }

    private const string OverlayNodeName = "__FlowFieldCostOverlay";

    [ExportGroup("References")]
    [Export]
    public NodePath Terrain3DPath { get; set; }

    [Export]
    public NodePath BoundsProviderPath { get; set; } 

    [Export]
    public NodePath BuildingContainerPath { get; set; } 

    [Export]
    public NodePath Team0GoalPath { get; set; }

    [Export]
    public NodePath Team1GoalPath { get; set; }

    [ExportGroup("Bake Settings")]
    [Export(PropertyHint.Range, "1,64,1")]
    public int CellSize { get; set; } = 4;

    [Export(PropertyHint.Range, "1,256,1")]
    public int TileSize { get; set; } = 8;

    [Export(PropertyHint.Range, "1,512,1")]
    public int NavTileSize { get; set; } = 128;

    [Export(PropertyHint.Range, "0,8,1")]
    public int NeighborRadius { get; set; } = 2;

    [ExportGroup("Brush")]
    [Export(PropertyHint.Range, "1,32,1")]
    public int BrushRadiusCells { get; set; } = 2;

    [Export(PropertyHint.Range, "1,255,1")]
    public int BrushCostValue { get; set; } = 16;

    [Export]
    public BrushMode PaintMode { get; set; } = BrushMode.Paint;

    [ExportGroup("Data")]
    [Export]
    public MapFlowFieldData BakedData { get; set; }

    [ExportGroup("Export")]
    [Export(PropertyHint.Dir)]
    public string CSharpExportPath { get; set; }

    [ExportGroup("Overlay")]
    private bool _showCostOverlay = true;
    [Export]
    public bool ShowCostOverlay
    {
        get => _showCostOverlay;
        set
        {
            if (_showCostOverlay == value)
                return;

            _showCostOverlay = value;
            if (Engine.IsEditorHint() && IsInsideTree())
                RefreshOverlayFromCostfield();
        }
    }

    [Export]
    public float _overlayTransparency = 0.5f;

    private float _overlayYOffset = 0.25f;
    [Export(PropertyHint.Range, "0.01,100.0,0.01")]
    public float OverlayYOffset
    {
        get => _overlayYOffset;
        set
        {
            float clamped = Mathf.Max(0.01f, value);
            if (Mathf.IsEqualApprox(_overlayYOffset, clamped))
                return;

            _overlayYOffset = clamped;
            if (Engine.IsEditorHint() && IsInsideTree())
                RefreshOverlayPlacement();
        }
    }

    private MeshInstance3D _overlayMesh;
    private StandardMaterial3D _overlayMaterial;
    private ImageTexture _overlayTexture;
    private bool _showCosts;
    [Export]
    public bool ShowCosts
    {
        get => _showCosts;
        set
        {
            if (_showCosts == value)
                return;

            _showCosts = value;
            MarkCostComponentsDirty();
            UpdateProcessState();
        }
    }

    [ExportGroup("Flow Arrows")]
    private bool _showFlowArrows;
    [Export]
    public bool ShowFlowArrows
    {
        get => _showFlowArrows;
        set
        {
            if (_showFlowArrows == value)
                return;

            _showFlowArrows = value;
            MarkFlowArrowCacheDirty();
            UpdateProcessState();
        }
    }

    private FlowArrowTeamSelection _flowArrowTeam = FlowArrowTeamSelection.Team0;
    [Export]
    public FlowArrowTeamSelection FlowArrowTeam
    {
        get => _flowArrowTeam;
        set
        {
            if (_flowArrowTeam == value)
                return;

            _flowArrowTeam = value;
            MarkFlowArrowCacheDirty();
        }
    }

    private int _flowArrowStride = 2;
    [Export(PropertyHint.Range, "1,64,1")]
    public int FlowArrowStride
    {
        get => _flowArrowStride;
        set => _flowArrowStride = Math.Max(1, value);
    }

    private int _maxFlowArrows = 10000;
    [Export(PropertyHint.Range, "0,50000,1")]
    public int MaxFlowArrows
    {
        get => _maxFlowArrows;
        set => _maxFlowArrows = Math.Max(0, value);
    }

    private float _flowArrowLengthScale = 2f;

    [Export]
    public bool FlowArrowUseTerrainHeight { get; set; }

    private int _minComponentAreaCells = 4;
    private int _maxComponentLabels = 512;

    private int _labelTextSize = 512;

    private float _outlineThickness = 0.8f;

    private static readonly Color ComponentLabelColor = Colors.White;
    private static readonly Color ComponentLabelOutlineColor = new(0f, 0f, 0f, 0.95f);

    private CostConnectedComponents.Component[] _cachedCostComponents = Array.Empty<CostConnectedComponents.Component>();
    private int[] _cachedComponentLabelOrder = Array.Empty<int>();

    private Image _cachedComponentSourceImage;
    private int _cachedComponentWidth = -1;
    private int _cachedComponentHeight = -1;
    private Vector3I _cachedComponentWorldOrigin = new(int.MinValue, int.MinValue, int.MinValue);
    private int _cachedComponentCellSize = -1;
    private float _cachedComponentWorldY = float.NaN;
    private int _cachedComponentMinArea = -1;
    private bool _costComponentsDirty = true;

    private MapFlowFieldData _cachedFlowArrowData;
    private Image _cachedFlowArrowSourceImage;
    private byte[] _cachedFlowArrowBytes = Array.Empty<byte>();
    private int _cachedFlowArrowWidth = -1;
    private int _cachedFlowArrowHeight = -1;
    private int _cachedFlowArrowCellSize = -1;
    private Vector2I _cachedFlowArrowFieldSize = new(int.MinValue, int.MinValue);
    private Vector3I _cachedFlowArrowWorldOrigin = new(int.MinValue, int.MinValue, int.MinValue);
    private FlowArrowTeamSelection _cachedFlowArrowTeam = (FlowArrowTeamSelection)(-1);
    private bool _flowArrowCacheDirty = true;

    private Node _cachedTerrainNode;
    private GodotObject _cachedTerrainData;

    private static readonly Color Team0FlowArrowColor = Colors.Lime;
    private static readonly Color Team1FlowArrowColor = Colors.Crimson;

    public override void _EnterTree()
    {
        AddToGroup("FlowFieldBakeNode");
    }

    public override void _Ready()
    {
        UpdateProcessState();

        if (!Engine.IsEditorHint())
        {
            MarkCostComponentsDirty();
            return;
        }

        if (!EnsureBakedData(out string error))
        {
            GD.PrintErr($"[FlowFieldBakeNode] {error}");
            return;
        }

        EnsureOverlayReady();
        RefreshOverlayFromCostfield();
    }

    public Node ResolveTerrainNode()
    {
        return HasPath(Terrain3DPath) ? GetNodeOrNull<Node>(Terrain3DPath) : null;
    }

    public Node ResolveBoundsProviderNode()
    {
        return HasPath(BoundsProviderPath) ? GetNodeOrNull<Node>(BoundsProviderPath) : null;
    }

    public Node ResolveBuildingContainerNode()
    {
        return HasPath(BuildingContainerPath) ? GetNodeOrNull<Node>(BuildingContainerPath) : null;
    }

    public bool TryCreateFimConfig(out FimConfig config, out string error)
    {
        config = null;
        error = string.Empty;

        Node boundsProvider = ResolveBoundsProviderNode();
        if (boundsProvider == null)
        {
            error = "BoundsProviderPath is not set or invalid.";
            return false;
        }

        Rect2I bounds = boundsProvider.Get("bounds").AsRect2I();
        if (bounds.Size.X <= 0 || bounds.Size.Y <= 0)
        {
            error = $"Bounds provider returned invalid bounds: {bounds}.";
            return false;
        }

        if (CellSize <= 0)
        {
            error = $"CellSize must be > 0 (current={CellSize}).";
            return false;
        }

        if (bounds.Size.X % CellSize != 0 || bounds.Size.Y % CellSize != 0)
        {
            error = $"Bounds size {bounds.Size} must be divisible by CellSize={CellSize}.";
            return false;
        }

        config = new FimConfig
        {
            StepScale = 1.0f,
            InfinityValue = 1e9f,
            MinimumSlowness = 1f,
            ItersPerSweep = 16,
            NeighborItersPerSweep = 1,
            ParallelTileThreshold = 2,
            MaxDegreeOfParallelism = Math.Max(1, System.Environment.ProcessorCount / 2),
            Epsilon = 1e-5f,
            UseDiagonalStencil = true,
            EnqueueDiagonalNeighborTiles = false,
            TileSize = Math.Max(1, TileSize),
            NavTileSize = Math.Max(1, NavTileSize),
            CellSize = CellSize,
            FieldSize = new Vector2I(bounds.Size.X, bounds.Size.Y),
            WorldOrigin = new Vector3I(bounds.Position.X, 0, bounds.Position.Y),
        };

        return true;
    }

    public bool EnsureBakedData(out string error)
    {
        error = string.Empty;

        if (!TryCreateFimConfig(out FimConfig config, out error))
            return false;

        if (BakedData == null)
            BakedData = new MapFlowFieldData();

        BakedData.CellSize = config.CellSize;
        BakedData.FieldSize = config.FieldSize;
        BakedData.WorldOrigin = config.WorldOrigin;

        int width = config.FieldSize.X / config.CellSize;
        int height = config.FieldSize.Y / config.CellSize;
        BakedData.CostfieldR8 = EnsureImage(BakedData.CostfieldR8, width, height, fillValue: 1);
        MarkCostComponentsDirty();
        MarkFlowArrowCacheDirty();

        return true;
    }

    public bool RebuildCostfieldFromTerrain(out string error)
    {
        error = string.Empty;
        if (!EnsureBakedData(out error))
            return false;

        if (!TryCreateFimConfig(out FimConfig config, out error))
            return false;

        Node terrainNode = ResolveTerrainNode();
        if (terrainNode == null)
        {
            error = "Terrain3DPath is not set or invalid.";
            return false;
        }

        GodotObject terrainData = terrainNode.Get("data").AsGodotObject();
        if (terrainData == null)
        {
            error = "Terrain3D node has no data object.";
            return false;
        }

        int width = config.FieldSize.X / config.CellSize;
        int height = config.FieldSize.Y / config.CellSize;
        byte[] costs = new byte[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float worldX = config.WorldOrigin.X + (x + 0.5f) * config.CellSize;
                float worldZ = config.WorldOrigin.Z + (y + 0.5f) * config.CellSize;
                bool walkable = terrainData.Call("get_control_navigation", new Vector3(worldX, 0f, worldZ)).AsBool();
                costs[y * width + x] = walkable ? (byte)1 : byte.MaxValue;
            }
        }

        BakedData.CostfieldR8 = Image.CreateFromData(width, height, false, Image.Format.R8, costs);
        RefreshOverlayFromCostfield();
        return true;
    }

    public bool TryGetGoalCells(FimConfig config, out Vector2I team0GoalCell, out Vector2I team1GoalCell, out string error)
    {
        team0GoalCell = new Vector2I(-1, -1);
        team1GoalCell = new Vector2I(-1, -1);
        error = string.Empty;

        if (!HasPath(Team0GoalPath) || GetNodeOrNull<Node3D>(Team0GoalPath) is not Node3D team0GoalNode)
        {
            error = "Team0GoalPath is not set or does not resolve to a Node3D.";
            return false;
        }

        if (!HasPath(Team1GoalPath) || GetNodeOrNull<Node3D>(Team1GoalPath) is not Node3D team1GoalNode)
        {
            error = "Team1GoalPath is not set or does not resolve to a Node3D.";
            return false;
        }

        team0GoalCell = WorldToCell(team0GoalNode.GlobalPosition, config);
        team1GoalCell = WorldToCell(team1GoalNode.GlobalPosition, config);

        int width = config.FieldSize.X / config.CellSize;
        int height = config.FieldSize.Y / config.CellSize;
        if (!IsCellInBounds(team0GoalCell.X, team0GoalCell.Y, width, height) ||
            !IsCellInBounds(team1GoalCell.X, team1GoalCell.Y, width, height))
        {
            error = $"Goal cells must be in-bounds. Team0={team0GoalCell}, Team1={team1GoalCell}, grid={width}x{height}.";
            return false;
        }

        return true;
    }

    public bool TryPaintAtWorldPoint(Vector3 worldPoint, out int touchedCells, out string error)
    {
        touchedCells = 0;
        error = string.Empty;

        if (!EnsureBakedData(out error))
            return false;

        if (!BakedData.TryGetGridSize(out int width, out int height))
        {
            error = "BakedData does not contain a valid grid mapping.";
            return false;
        }

        if (!TryWorldToCell(worldPoint, out int centerX, out int centerY))
            return true;

        byte[] raw = BakedData.CostfieldR8.GetData();
        if (raw.Length != width * height)
        {
            error = $"Costfield byte length mismatch. Expected {width * height}, got {raw.Length}.";
            return false;
        }

        int centerIdx = centerY * width + centerX;
        if ((uint)centerIdx >= (uint)raw.Length)
            return true;

        if (PaintMode == BrushMode.Sample)
        {
            int sampled = raw[centerIdx];
            BrushCostValue = Mathf.Clamp(sampled, 1, 255);
            return true;
        }

        int radius = Math.Max(1, BrushRadiusCells);
        byte targetCost = PaintMode == BrushMode.Erase
            ? (byte)1
            : (byte)Mathf.Clamp(BrushCostValue, 1, 255);

        int r2 = radius * radius;
        for (int dy = -radius; dy <= radius; dy++)
        {
            int y = centerY + dy;
            if ((uint)y >= (uint)height)
                continue;

            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = centerX + dx;
                if ((uint)x >= (uint)width)
                    continue;
                if (dx * dx + dy * dy > r2)
                    continue;

                int idx = y * width + x;
                if (raw[idx] == targetCost)
                    continue;

                raw[idx] = targetCost;
                touchedCells++;
            }
        }

        if (touchedCells > 0)
        {
            BakedData.CostfieldR8 = Image.CreateFromData(width, height, false, Image.Format.R8, raw);
            RefreshOverlayFromCostfield();
        }

        return true;
    }

    public bool TryGetBakedDataResourcePath(out string outputPath, out string error)
    {
        outputPath = string.Empty;
        error = string.Empty;

        if (BakedData == null)
        {
            error = "BakedData is not assigned.";
            return false;
        }

        string normalized = NormalizePath(BakedData.ResourcePath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            error = "BakedData must be an external resource with a valid ResourcePath.";
            return false;
        }

        if (!normalized.StartsWith("res://", StringComparison.Ordinal) ||
            normalized.Contains("::", StringComparison.Ordinal))
        {
            error = $"BakedData.ResourcePath must be a writable project path. Got '{normalized}'.";
            return false;
        }

        outputPath = normalized;
        return true;
    }

    public void RefreshOverlayFromCostfield()
    {
        MarkCostComponentsDirty();
        MarkFlowArrowCacheDirty();
        RebuildCostComponentsIfNeeded(force: true);

        if (!Engine.IsEditorHint())
            return;

        EnsureOverlayReady();

        if (_overlayMesh == null)
            return;

        _overlayMesh.Visible = ShowCostOverlay;
        if (!ShowCostOverlay || BakedData?.CostfieldR8 == null)
            return;

        if (!BakedData.TryGetGridSize(out _, out _))
            return;

        if (!TryCreateFimConfig(out FimConfig config, out _))
            return;

        PlaceOverlay(config);

        Image heatmapImage = BuildHeatmapImage(BakedData.CostfieldR8);
        int heatW = heatmapImage.GetWidth();
        int heatH = heatmapImage.GetHeight();
        bool needsRecreate = _overlayTexture == null ||
            _overlayTexture.GetWidth() != heatW || _overlayTexture.GetHeight() != heatH;

        if (needsRecreate)
        {
            _overlayTexture = ImageTexture.CreateFromImage(heatmapImage);
            _overlayMaterial.AlbedoTexture = _overlayTexture;
        }
        else
        {
            _overlayTexture.Update(heatmapImage);
        }
    }

    public override void _Process(double delta)
    {
        if (!_showCosts && !_showFlowArrows)
            return;

        using var cfg = DebugDraw3D.NewScopedConfig()
            .SetNoDepthTest(true)
            .SetThickness(Mathf.Max(0.001f, _outlineThickness))
            .SetTextOutlineColor(ComponentLabelOutlineColor)
            .SetTextOutlineSize(Math.Max(1, _labelTextSize / 3));

        if (_showCosts)
            DrawCostComponentsDebug();

        if (_showFlowArrows && Engine.IsEditorHint())
            DrawFlowArrows();
    }

    private void DrawCostComponentsDebug()
    {
        RebuildCostComponentsIfNeeded();
        if (_cachedCostComponents.Length == 0)
            return;

        for (int i = 0; i < _cachedCostComponents.Length; i++)
        {
            CostConnectedComponents.Component component = _cachedCostComponents[i];
            if (component.OutlineLinesWorld.Length == 0)
                continue;

            DebugDraw3D.DrawLines(component.OutlineLinesWorld, ToOutlineColor(component.Cost));
        }

        int labelLimit = _maxComponentLabels <= 0
            ? _cachedComponentLabelOrder.Length
            : Math.Min(_maxComponentLabels, _cachedComponentLabelOrder.Length);

        for (int i = 0; i < labelLimit; i++)
        {
            CostConnectedComponents.Component component = _cachedCostComponents[_cachedComponentLabelOrder[i]];
            DebugDraw3D.DrawText(component.LabelWorldPosition, component.Cost.ToString(), _labelTextSize, ComponentLabelColor);
        }
    }

    private void DrawFlowArrows()
    {
        if (!TryGetFlowArrowBytes(
                out byte[] flowBytes,
                out int width,
                out int height,
                out int cellSize,
                out Vector3I worldOrigin,
                out Vector2I goalCell,
                out FlowArrowTeamSelection selectedTeam))
        {
            return;
        }

        int stride = Math.Max(1, _flowArrowStride);
        int maxArrows = Math.Max(0, _maxFlowArrows);
        if (maxArrows == 0)
            return;

        float arrowLength = Mathf.Max(0.001f, 0.45f * cellSize * _flowArrowLengthScale);
        Color arrowColor = selectedTeam == FlowArrowTeamSelection.Team0 ? Team0FlowArrowColor : Team1FlowArrowColor;

        GodotObject terrainData = null;
        bool useTerrainHeight = FlowArrowUseTerrainHeight && TryGetCachedTerrainData(out terrainData);
        int arrowsDrawn = 0;

        for (int y = 0; y < height && arrowsDrawn < maxArrows; y += stride)
        {
            int row = y * width;
            for (int x = 0; x < width && arrowsDrawn < maxArrows; x += stride)
            {
                if (x == goalCell.X && y == goalCell.Y)
                    continue;

                byte raw = flowBytes[row + x];
                var flags = (FlowFlags.FlowFieldFlags)((raw >> 4) & 0x0F);
                if ((flags & FlowFlags.FlowFieldFlags.Pathable) == 0)
                    continue;

                int dirIndex = raw & 0x0F;
                if ((uint)dirIndex >= (uint)DirectionLUT.Directions.Length)
                    continue;

                Vector2 dir = DirectionLUT.Directions[dirIndex];
                if (Mathf.IsZeroApprox(dir.LengthSquared()))
                    continue;

                float worldX = worldOrigin.X + (x + 0.5f) * cellSize;
                float worldZ = worldOrigin.Z + (y + 0.5f) * cellSize;
                float worldY = GlobalPosition.Y + _overlayYOffset;

                if (useTerrainHeight && TrySampleTerrainHeight(terrainData, worldX, worldZ, out float terrainHeight))
                    worldY = terrainHeight + _overlayYOffset;

                Vector3 from = new(worldX, worldY, worldZ);
                Vector3 to = from + new Vector3(dir.X * arrowLength, 0f, dir.Y * arrowLength);
                DebugDraw3D.DrawArrow(from, to, arrowColor, 0.2f, true);
                arrowsDrawn++;
            }
        }
    }

    private void RefreshOverlayPlacement()
    {
        if (!Engine.IsEditorHint())
            return;

        EnsureOverlayReady();
        if (_overlayMesh == null)
            return;

        if (!TryCreateFimConfig(out FimConfig config, out _))
            return;

        PlaceOverlay(config);
    }

    private void EnsureOverlayReady()
    {
        if (_overlayMesh == null || !GodotObject.IsInstanceValid(_overlayMesh))
            _overlayMesh = GetNodeOrNull<MeshInstance3D>(OverlayNodeName);

        if (_overlayMesh == null)
        {
            _overlayMesh = new MeshInstance3D
            {
                Name = OverlayNodeName
            };
            AddChild(_overlayMesh);
        }

        if (_overlayMaterial == null || !GodotObject.IsInstanceValid(_overlayMaterial))
            _overlayMaterial = new StandardMaterial3D();

        _overlayMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        _overlayMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _overlayMaterial.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        _overlayMaterial.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
        _overlayMaterial.TextureRepeat = false;
        _overlayMaterial.AlbedoColor = new Color(1f, 1f, 1f, 0.1f);
        _overlayMesh.MaterialOverride = _overlayMaterial;

        if (_overlayMesh.Mesh == null)
            _overlayMesh.Mesh = new PlaneMesh();
    }

    private void PlaceOverlay(FimConfig config)
    {
        if (_overlayMesh.Mesh is not PlaneMesh plane)
            return;

        plane.Size = new Vector2(config.FieldSize.X, config.FieldSize.Y);

        float worldCenterX = config.WorldOrigin.X + config.FieldSize.X * 0.5f;
        float worldCenterZ = config.WorldOrigin.Z + config.FieldSize.Y * 0.5f;
        _overlayMesh.GlobalPosition = new Vector3(worldCenterX, GlobalPosition.Y + OverlayYOffset, worldCenterZ);
    }

    private bool TryWorldToCell(Vector3 worldPoint, out int cellX, out int cellY)
    {
        cellX = 0;
        cellY = 0;

        if (BakedData == null || !BakedData.TryGetGridSize(out int width, out int height))
            return false;

        float relX = worldPoint.X - BakedData.WorldOrigin.X;
        float relZ = worldPoint.Z - BakedData.WorldOrigin.Z;
        cellX = Mathf.FloorToInt(relX / BakedData.CellSize);
        cellY = Mathf.FloorToInt(relZ / BakedData.CellSize);
        return IsCellInBounds(cellX, cellY, width, height);
    }

    private static bool IsCellInBounds(int x, int y, int width, int height)
    {
        return (uint)x < (uint)width && (uint)y < (uint)height;
    }

    private static Vector2I WorldToCell(Vector3 worldPoint, FimConfig config)
    {
        float relX = worldPoint.X - config.WorldOrigin.X;
        float relZ = worldPoint.Z - config.WorldOrigin.Z;
        int cellX = Mathf.FloorToInt(relX / config.CellSize);
        int cellY = Mathf.FloorToInt(relZ / config.CellSize);
        return new Vector2I(cellX, cellY);
    }

    private static Image EnsureImage(Image source, int width, int height, byte fillValue)
    {
        if (source != null && source.GetWidth() == width && source.GetHeight() == height && source.GetFormat() == Image.Format.R8)
            return source;

        byte[] bytes = new byte[width * height];
        Array.Fill(bytes, fillValue);
        return Image.CreateFromData(width, height, false, Image.Format.R8, bytes);
    }

    private static Image BuildHeatmapImage(Image costfieldR8)
    {
        int width = costfieldR8.GetWidth();
        int height = costfieldR8.GetHeight();
        byte[] costs = costfieldR8.GetData();

        Image heatmap = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                byte cost = costs[row + x];
                Color color = ToHeatColor(cost);
                heatmap.SetPixel(x, y, color);
            }
        }

        return heatmap;
    }

    private void UpdateProcessState()
    {
        SetProcess(_showCosts || _showFlowArrows);
    }

    private void MarkFlowArrowCacheDirty()
    {
        _flowArrowCacheDirty = true;
    }

    private void ClearFlowArrowCache()
    {
        _cachedFlowArrowData = null;
        _cachedFlowArrowSourceImage = null;
        _cachedFlowArrowBytes = Array.Empty<byte>();
        _cachedFlowArrowWidth = -1;
        _cachedFlowArrowHeight = -1;
        _cachedFlowArrowCellSize = -1;
        _cachedFlowArrowFieldSize = new Vector2I(int.MinValue, int.MinValue);
        _cachedFlowArrowWorldOrigin = new Vector3I(int.MinValue, int.MinValue, int.MinValue);
        _cachedFlowArrowTeam = (FlowArrowTeamSelection)(-1);
        _flowArrowCacheDirty = false;
    }

    private bool TryGetFlowArrowBytes(
        out byte[] flowBytes,
        out int width,
        out int height,
        out int cellSize,
        out Vector3I worldOrigin,
        out Vector2I goalCell,
        out FlowArrowTeamSelection selectedTeam)
    {
        flowBytes = Array.Empty<byte>();
        width = 0;
        height = 0;
        cellSize = 0;
        worldOrigin = Vector3I.Zero;
        goalCell = new Vector2I(-1, -1);
        selectedTeam = _flowArrowTeam;

        MapFlowFieldData data = BakedData;
        if (data == null || !data.TryGetGridSize(out width, out height))
        {
            ClearFlowArrowCache();
            return false;
        }

        selectedTeam = _flowArrowTeam;
        Image sourceImage = selectedTeam == FlowArrowTeamSelection.Team0 ? data.FlowTeam0R8 : data.FlowTeam1R8;
        goalCell = selectedTeam == FlowArrowTeamSelection.Team0 ? data.Team0GoalCell : data.Team1GoalCell;
        cellSize = data.CellSize;
        worldOrigin = data.WorldOrigin;

        if (sourceImage == null || sourceImage.GetFormat() != Image.Format.R8)
        {
            ClearFlowArrowCache();
            return false;
        }

        bool needsDecode = _flowArrowCacheDirty ||
                           !ReferenceEquals(data, _cachedFlowArrowData) ||
                           !ReferenceEquals(sourceImage, _cachedFlowArrowSourceImage) ||
                           selectedTeam != _cachedFlowArrowTeam ||
                           width != _cachedFlowArrowWidth ||
                           height != _cachedFlowArrowHeight ||
                           cellSize != _cachedFlowArrowCellSize ||
                           data.FieldSize != _cachedFlowArrowFieldSize ||
                           worldOrigin != _cachedFlowArrowWorldOrigin;

        if (needsDecode)
        {
            byte[] decoded = sourceImage.GetData();
            if (decoded == null || decoded.Length != width * height)
            {
                ClearFlowArrowCache();
                return false;
            }

            _cachedFlowArrowData = data;
            _cachedFlowArrowSourceImage = sourceImage;
            _cachedFlowArrowBytes = decoded;
            _cachedFlowArrowWidth = width;
            _cachedFlowArrowHeight = height;
            _cachedFlowArrowCellSize = cellSize;
            _cachedFlowArrowFieldSize = data.FieldSize;
            _cachedFlowArrowWorldOrigin = worldOrigin;
            _cachedFlowArrowTeam = selectedTeam;
            _flowArrowCacheDirty = false;
        }

        if (_cachedFlowArrowBytes == null || _cachedFlowArrowBytes.Length != width * height)
        {
            ClearFlowArrowCache();
            return false;
        }

        flowBytes = _cachedFlowArrowBytes;
        return true;
    }

    private bool TryGetCachedTerrainData(out GodotObject terrainData)
    {
        terrainData = null;

        if (_cachedTerrainData != null && GodotObject.IsInstanceValid(_cachedTerrainData))
        {
            terrainData = _cachedTerrainData;
            return true;
        }

        if (_cachedTerrainNode == null || !GodotObject.IsInstanceValid(_cachedTerrainNode))
            _cachedTerrainNode = ResolveTerrainNode();

        if (_cachedTerrainNode == null || !GodotObject.IsInstanceValid(_cachedTerrainNode))
        {
            _cachedTerrainData = null;
            return false;
        }

        GodotObject data = _cachedTerrainNode.Get("data").AsGodotObject();
        if (data == null || !GodotObject.IsInstanceValid(data))
        {
            _cachedTerrainData = null;
            return false;
        }

        _cachedTerrainData = data;
        terrainData = data;
        return true;
    }

    private static bool TrySampleTerrainHeight(GodotObject terrainData, float worldX, float worldZ, out float terrainHeight)
    {
        terrainHeight = 0f;
        if (terrainData == null)
            return false;

        Variant value = terrainData.Call("get_height", new Vector3(worldX, 0f, worldZ));
        if (value.VariantType == Variant.Type.Nil)
            return false;

        double sampled = value.AsDouble();
        if (double.IsNaN(sampled) || double.IsInfinity(sampled))
            return false;

        terrainHeight = (float)sampled;
        return true;
    }

    private void MarkCostComponentsDirty()
    {
        _costComponentsDirty = true;
    }

    private void ClearCostComponentCache()
    {
        _cachedCostComponents = Array.Empty<CostConnectedComponents.Component>();
        _cachedComponentLabelOrder = Array.Empty<int>();

        _cachedComponentSourceImage = null;
        _cachedComponentWidth = -1;
        _cachedComponentHeight = -1;
        _cachedComponentWorldOrigin = new Vector3I(int.MinValue, int.MinValue, int.MinValue);
        _cachedComponentCellSize = -1;
        _cachedComponentWorldY = float.NaN;
        _cachedComponentMinArea = -1;
    }

    private void RebuildCostComponentsIfNeeded(bool force = false)
    {
        if (!_showCosts)
            return;

        if (BakedData?.CostfieldR8 == null || !BakedData.TryGetGridSize(out int width, out int height))
        {
            ClearCostComponentCache();
            _costComponentsDirty = false;
            return;
        }

        Image sourceImage = BakedData.CostfieldR8;
        float worldY = GlobalPosition.Y + _overlayYOffset;

        bool needsRebuild = force ||
            _costComponentsDirty ||
            !ReferenceEquals(sourceImage, _cachedComponentSourceImage) ||
            width != _cachedComponentWidth ||
            height != _cachedComponentHeight ||
            BakedData.CellSize != _cachedComponentCellSize ||
            BakedData.WorldOrigin != _cachedComponentWorldOrigin ||
            !Mathf.IsEqualApprox(worldY, _cachedComponentWorldY) ||
            _minComponentAreaCells != _cachedComponentMinArea;

        if (!needsRebuild)
            return;

        _cachedCostComponents = CostConnectedComponents.Build(
            sourceImage.GetData(),
            width,
            height,
            BakedData.WorldOrigin,
            BakedData.CellSize,
            worldY,
            _minComponentAreaCells);

        RebuildComponentRenderCaches();

        _cachedComponentSourceImage = sourceImage;
        _cachedComponentWidth = width;
        _cachedComponentHeight = height;
        _cachedComponentCellSize = BakedData.CellSize;
        _cachedComponentWorldOrigin = BakedData.WorldOrigin;
        _cachedComponentWorldY = worldY;
        _cachedComponentMinArea = _minComponentAreaCells;
        _costComponentsDirty = false;
    }

    private void RebuildComponentRenderCaches()
    {
        int componentCount = _cachedCostComponents.Length;
        _cachedComponentLabelOrder = new int[componentCount];

        for (int i = 0; i < componentCount; i++)
            _cachedComponentLabelOrder[i] = i;

        Array.Sort(_cachedComponentLabelOrder, CompareComponentLabelOrder);
    }

    private int CompareComponentLabelOrder(int lhs, int rhs)
    {
        CostConnectedComponents.Component a = _cachedCostComponents[lhs];
        CostConnectedComponents.Component b = _cachedCostComponents[rhs];

        int areaCmp = b.AreaCells.CompareTo(a.AreaCells);
        if (areaCmp != 0)
            return areaCmp;

        int costCmp = a.Cost.CompareTo(b.Cost);
        if (costCmp != 0)
            return costCmp;

        if (a.LabelCell.Y != b.LabelCell.Y)
            return a.LabelCell.Y.CompareTo(b.LabelCell.Y);

        return a.LabelCell.X.CompareTo(b.LabelCell.X);
    }

    private static Color ToHeatColor(byte cost)
    {
        // Fixed mapping keeps colors stable while painting:
        // same byte cost always renders as the same color.
        float t = (cost - 1) / 253f;
        t = Mathf.Clamp(t, 0f, 1f);
        t = Mathf.Pow(t, 0.75f);

        Color low = new(0.10f, 0.74f, 0.24f, 0.90f);   // green (cheap)
        Color mid = new(0.96f, 0.85f, 0.20f, 0.90f);   // yellow (medium)
        Color high = new(0.95f, 0.52f, 0.15f, 0.92f);  // orange (expensive)
        Color max = new(0.88f, 0.20f, 0.16f, 0.92f);   // red (max walkable cost)

        if (t <= 0.5f)
            return low.Lerp(mid, t * 2f);
        if (t <= 0.85f)
            return mid.Lerp(high, (t - 0.5f) / 0.35f);
        return high.Lerp(max, (t - 0.85f) / 0.15f);
    }

    private static Color ToOutlineColor(byte cost)
    {
        Color heat = ToHeatColor(cost);
        return new Color(
            Mathf.Clamp(heat.R * 0.7f, 0f, 1f),
            Mathf.Clamp(heat.G * 0.7f, 0f, 1f),
            Mathf.Clamp(heat.B * 0.7f, 0f, 1f),
            0.98f);
    }

    private static bool HasPath(NodePath path)
    {
        return !string.IsNullOrWhiteSpace(path.ToString());
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}

internal static class CostConnectedComponents
{
    public readonly struct Component
    {
        public Component(byte cost, int areaCells, Rect2I bounds, Vector2I labelCell, Vector3 labelWorldPosition, Vector3[] outlineLinesWorld)
        {
            Cost = cost;
            AreaCells = areaCells;
            Bounds = bounds;
            LabelCell = labelCell;
            LabelWorldPosition = labelWorldPosition;
            OutlineLinesWorld = outlineLinesWorld ?? Array.Empty<Vector3>();
        }

        public byte Cost { get; }
        public int AreaCells { get; }
        public Rect2I Bounds { get; }
        public Vector2I LabelCell { get; }
        public Vector3 LabelWorldPosition { get; }
        public Vector3[] OutlineLinesWorld { get; }
    }

    public static Component[] Build(
        byte[] costs,
        int width,
        int height,
        Vector3I worldOrigin,
        int cellSize,
        float worldY,
        int minComponentAreaCells)
    {
        if (costs == null || width <= 0 || height <= 0 || cellSize <= 0)
            return Array.Empty<Component>();

        int cellCount = width * height;
        if (costs.Length < cellCount)
            return Array.Empty<Component>();

        int minArea = Math.Max(1, minComponentAreaCells);
        int[] componentIds = new int[cellCount];
        Array.Fill(componentIds, -1);

        int[] queue = new int[cellCount];
        int[] distances = new int[cellCount];
        var componentCells = new System.Collections.Generic.List<int>(Math.Min(cellCount, 1024));
        var components = new System.Collections.Generic.List<Component>();

        int componentId = 0;
        for (int idx = 0; idx < cellCount; idx++)
        {
            if (componentIds[idx] != -1)
                continue;

            byte cost = costs[idx];
            int area = FloodFillComponent(
                costs,
                width,
                height,
                idx,
                cost,
                componentId,
                componentIds,
                queue,
                componentCells,
                out Rect2I bounds);

            if (area >= minArea)
            {
                Vector2I labelCell = FindLabelCell(componentId, width, height, componentIds, queue, distances, componentCells, bounds);
                Vector3 labelWorld = CellCenterToWorld(labelCell.X, labelCell.Y, worldOrigin, cellSize, worldY);
                Vector3[] outlineLines = BuildOutlineLinesWorld(componentId, width, height, componentIds, componentCells, worldOrigin, cellSize, worldY);
                components.Add(new Component(cost, area, bounds, labelCell, labelWorld, outlineLines));
            }

            componentId++;
        }

        return components.ToArray();
    }

    private static int FloodFillComponent(
        byte[] costs,
        int width,
        int height,
        int startIdx,
        byte cost,
        int componentId,
        int[] componentIds,
        int[] queue,
        System.Collections.Generic.List<int> componentCells,
        out Rect2I bounds)
    {
        componentCells.Clear();

        int head = 0;
        int tail = 0;
        queue[tail++] = startIdx;
        componentIds[startIdx] = componentId;

        int minX = width - 1;
        int minY = height - 1;
        int maxX = 0;
        int maxY = 0;

        while (head < tail)
        {
            int idx = queue[head++];
            componentCells.Add(idx);

            int x = idx % width;
            int y = idx / width;
            if (x < minX)
                minX = x;
            if (y < minY)
                minY = y;
            if (x > maxX)
                maxX = x;
            if (y > maxY)
                maxY = y;

            if (x > 0)
                TryVisitNeighbor(idx - 1, cost, costs, componentId, componentIds, queue, ref tail);
            if (x + 1 < width)
                TryVisitNeighbor(idx + 1, cost, costs, componentId, componentIds, queue, ref tail);
            if (y > 0)
                TryVisitNeighbor(idx - width, cost, costs, componentId, componentIds, queue, ref tail);
            if (y + 1 < height)
                TryVisitNeighbor(idx + width, cost, costs, componentId, componentIds, queue, ref tail);
        }

        bounds = new Rect2I(minX, minY, maxX - minX + 1, maxY - minY + 1);
        return componentCells.Count;
    }

    private static void TryVisitNeighbor(
        int neighborIdx,
        byte cost,
        byte[] costs,
        int componentId,
        int[] componentIds,
        int[] queue,
        ref int tail)
    {
        if (componentIds[neighborIdx] != -1 || costs[neighborIdx] != cost)
            return;

        componentIds[neighborIdx] = componentId;
        queue[tail++] = neighborIdx;
    }

    private static Vector2I FindLabelCell(
        int componentId,
        int width,
        int height,
        int[] componentIds,
        int[] queue,
        int[] distances,
        System.Collections.Generic.List<int> componentCells,
        Rect2I bounds)
    {
        for (int i = 0; i < componentCells.Count; i++)
            distances[componentCells[i]] = -1;

        int head = 0;
        int tail = 0;

        for (int i = 0; i < componentCells.Count; i++)
        {
            int idx = componentCells[i];
            if (!IsBoundaryCell(idx, componentId, width, height, componentIds))
                continue;

            distances[idx] = 0;
            queue[tail++] = idx;
        }

        if (tail == 0)
            return IndexToCell(componentCells[0], width);

        float centerX = bounds.Position.X + (bounds.Size.X - 1) * 0.5f;
        float centerY = bounds.Position.Y + (bounds.Size.Y - 1) * 0.5f;
        int bestIdx = componentCells[0];
        int bestDistance = -1;
        float bestCenterDistanceSq = float.MaxValue;

        while (head < tail)
        {
            int idx = queue[head++];
            int distance = distances[idx];
            float centerDistanceSq = DistanceToCenterSq(idx, width, centerX, centerY);

            if (IsBetterCandidate(idx, distance, centerDistanceSq, bestIdx, bestDistance, bestCenterDistanceSq, width))
            {
                bestIdx = idx;
                bestDistance = distance;
                bestCenterDistanceSq = centerDistanceSq;
            }

            int x = idx % width;
            int y = idx / width;

            if (x > 0)
                TryVisitDistanceNeighbor(idx - 1, componentId, componentIds, distances, distance + 1, queue, ref tail);
            if (x + 1 < width)
                TryVisitDistanceNeighbor(idx + 1, componentId, componentIds, distances, distance + 1, queue, ref tail);
            if (y > 0)
                TryVisitDistanceNeighbor(idx - width, componentId, componentIds, distances, distance + 1, queue, ref tail);
            if (y + 1 < height)
                TryVisitDistanceNeighbor(idx + width, componentId, componentIds, distances, distance + 1, queue, ref tail);
        }

        return IndexToCell(bestIdx, width);
    }

    private static void TryVisitDistanceNeighbor(
        int neighborIdx,
        int componentId,
        int[] componentIds,
        int[] distances,
        int newDistance,
        int[] queue,
        ref int tail)
    {
        if (componentIds[neighborIdx] != componentId || distances[neighborIdx] >= 0)
            return;

        distances[neighborIdx] = newDistance;
        queue[tail++] = neighborIdx;
    }

    private static bool IsBoundaryCell(int idx, int componentId, int width, int height, int[] componentIds)
    {
        int x = idx % width;
        int y = idx / width;

        if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
            return true;

        return componentIds[idx - 1] != componentId ||
               componentIds[idx + 1] != componentId ||
               componentIds[idx - width] != componentId ||
               componentIds[idx + width] != componentId;
    }

    private static bool IsBetterCandidate(
        int idx,
        int distance,
        float centerDistanceSq,
        int bestIdx,
        int bestDistance,
        float bestCenterDistanceSq,
        int width)
    {
        if (distance > bestDistance)
            return true;
        if (distance < bestDistance)
            return false;
        if (centerDistanceSq < bestCenterDistanceSq)
            return true;
        if (centerDistanceSq > bestCenterDistanceSq)
            return false;

        int x = idx % width;
        int y = idx / width;
        int bestX = bestIdx % width;
        int bestY = bestIdx / width;
        if (y != bestY)
            return y < bestY;

        return x < bestX;
    }

    private static float DistanceToCenterSq(int idx, int width, float centerX, float centerY)
    {
        int x = idx % width;
        int y = idx / width;
        float dx = x - centerX;
        float dy = y - centerY;
        return dx * dx + dy * dy;
    }

    private static Vector3[] BuildOutlineLinesWorld(
        int componentId,
        int width,
        int height,
        int[] componentIds,
        System.Collections.Generic.List<int> componentCells,
        Vector3I worldOrigin,
        int cellSize,
        float worldY)
    {
        var lines = new System.Collections.Generic.List<Vector3>(componentCells.Count * 8);

        for (int i = 0; i < componentCells.Count; i++)
        {
            int idx = componentCells[i];
            int x = idx % width;
            int y = idx / width;

            if (y == 0 || componentIds[idx - width] != componentId)
                AddEdge(lines, x, y, x + 1, y, worldOrigin, cellSize, worldY);
            if (x == width - 1 || componentIds[idx + 1] != componentId)
                AddEdge(lines, x + 1, y, x + 1, y + 1, worldOrigin, cellSize, worldY);
            if (y == height - 1 || componentIds[idx + width] != componentId)
                AddEdge(lines, x + 1, y + 1, x, y + 1, worldOrigin, cellSize, worldY);
            if (x == 0 || componentIds[idx - 1] != componentId)
                AddEdge(lines, x, y + 1, x, y, worldOrigin, cellSize, worldY);
        }

        return lines.ToArray();
    }

    private static void AddEdge(
        System.Collections.Generic.List<Vector3> lines,
        int ax,
        int ay,
        int bx,
        int by,
        Vector3I worldOrigin,
        int cellSize,
        float worldY)
    {
        lines.Add(GridCornerToWorld(ax, ay, worldOrigin, cellSize, worldY));
        lines.Add(GridCornerToWorld(bx, by, worldOrigin, cellSize, worldY));
    }

    private static Vector2I IndexToCell(int idx, int width)
    {
        return new Vector2I(idx % width, idx / width);
    }

    private static Vector3 CellCenterToWorld(int x, int y, Vector3I worldOrigin, int cellSize, float worldY)
    {
        return new Vector3(
            worldOrigin.X + (x + 0.5f) * cellSize,
            worldY,
            worldOrigin.Z + (y + 0.5f) * cellSize);
    }

    private static Vector3 GridCornerToWorld(int x, int y, Vector3I worldOrigin, int cellSize, float worldY)
    {
        return new Vector3(
            worldOrigin.X + x * cellSize,
            worldY,
            worldOrigin.Z + y * cellSize);
    }
}
