#if TOOLS
using System;
using Godot;

namespace SpacetimeDB.Addons.FlowFieldBaker;

[Tool]
public partial class FlowFieldBakeEditorPlugin : EditorPlugin
{
    private const int GuiInputPass = 0;
    private const int GuiInputStop = 1;

    private FlowFieldBakeNode _activeNode;

    private HBoxContainer _toolbar;
    private SpinBox _radiusSpin;
    private SpinBox _costSpin;
    private OptionButton _modeOption;
    private CheckBox _showFlowArrowsToggle;
    private OptionButton _flowArrowTeamOption;
    private Button _rebuildButton;
    private Button _bakeButton;
    private Button _exportButton;
    private Label _statusLabel;

    public override void _EnterTree()
    {
        CreateToolbar();
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, _toolbar);
        _toolbar.Visible = false;
    }

    public override void _ExitTree()
    {
        if (_toolbar != null)
        {
            RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, _toolbar);
            _toolbar.QueueFree();
            _toolbar = null;
        }
    }

    public override bool _Handles(GodotObject @object)
    {
        return @object is FlowFieldBakeNode;
    }

    public override void _Edit(GodotObject @object)
    {
        _activeNode = @object as FlowFieldBakeNode;
        SyncToolbarFromNode();
        UpdateToolbarVisibility();
    }

    public override void _MakeVisible(bool visible)
    {
        if (_toolbar != null)
            _toolbar.Visible = visible && _activeNode != null;
    }

    public override int _Forward3DGuiInput(Camera3D viewportCamera, InputEvent @event)
    {
        if (_activeNode == null || !GodotObject.IsInstanceValid(_activeNode))
            return GuiInputPass;
        if (@event is not InputEventMouse mouseEvent)
            return GuiInputPass;

        if (Input.IsMouseButtonPressed(MouseButton.Right) ||
            Input.IsMouseButtonPressed(MouseButton.Middle) ||
            Input.IsKeyPressed(Key.Alt))
        {
            return GuiInputPass;
        }

        bool shouldPaint = false;
        if (@event is InputEventMouseButton mouseButton)
        {
            shouldPaint = mouseButton.ButtonIndex == MouseButton.Left && mouseButton.Pressed;
        }
        else if (@event is InputEventMouseMotion)
        {
            shouldPaint = Input.IsMouseButtonPressed(MouseButton.Left);
        }

        if (!shouldPaint)
            return GuiInputPass;

        ApplyToolbarToNode();

        if (!TryGetTerrainIntersection(viewportCamera, mouseEvent.Position, out Vector3 hitPosition))
            return GuiInputPass;

        if (!_activeNode.TryPaintAtWorldPoint(hitPosition, out int touchedCells, out string error))
        {
            SetStatus(error, isError: true);
            return GuiInputStop;
        }

        if (_activeNode.PaintMode == FlowFieldBakeNode.BrushMode.Sample)
        {
            SyncToolbarFromNode();
            SetStatus($"Sampled cost value: {_activeNode.BrushCostValue}", isError: false);
        }
        else if (touchedCells > 0)
        {
            SetStatus($"Painted {touchedCells} cells", isError: false);
        }

        return GuiInputStop;
    }

    private void CreateToolbar()
    {
        _toolbar = new HBoxContainer();
        _toolbar.CustomMinimumSize = new Vector2(1200, 0);

        var title = new Label { Text = "Flowfield Baker" };
        _toolbar.AddChild(title);

        _toolbar.AddChild(new VSeparator());

        _toolbar.AddChild(new Label { Text = "Radius" });
        _radiusSpin = new SpinBox
        {
            MinValue = 1,
            MaxValue = 32,
            Step = 1
        };
        _radiusSpin.ValueChanged += _ => ApplyToolbarToNode();
        _toolbar.AddChild(_radiusSpin);

        _toolbar.AddChild(new Label { Text = "Cost" });
        _costSpin = new SpinBox
        {
            MinValue = 1,
            MaxValue = 255,
            Step = 1
        };
        _costSpin.ValueChanged += _ => ApplyToolbarToNode();
        _toolbar.AddChild(_costSpin);

        _toolbar.AddChild(new Label { Text = "Mode" });
        _modeOption = new OptionButton();
        _modeOption.AddItem("Paint", (int)FlowFieldBakeNode.BrushMode.Paint);
        _modeOption.AddItem("Erase", (int)FlowFieldBakeNode.BrushMode.Erase);
        _modeOption.AddItem("Sample", (int)FlowFieldBakeNode.BrushMode.Sample);
        _modeOption.ItemSelected += _ => ApplyToolbarToNode();
        _toolbar.AddChild(_modeOption);

        _toolbar.AddChild(new VSeparator());

        _showFlowArrowsToggle = new CheckBox { Text = "Show Arrows" };
        _showFlowArrowsToggle.Toggled += _ => ApplyToolbarToNode();
        _toolbar.AddChild(_showFlowArrowsToggle);

        _toolbar.AddChild(new Label { Text = "Arrow Team" });
        _flowArrowTeamOption = new OptionButton();
        _flowArrowTeamOption.AddItem("Team0", (int)FlowFieldBakeNode.FlowArrowTeamSelection.Team0);
        _flowArrowTeamOption.AddItem("Team1", (int)FlowFieldBakeNode.FlowArrowTeamSelection.Team1);
        _flowArrowTeamOption.ItemSelected += _ => ApplyToolbarToNode();
        _toolbar.AddChild(_flowArrowTeamOption);

        _toolbar.AddChild(new VSeparator());

        _rebuildButton = new Button { Text = "Rebuild From Terrain" };
        _rebuildButton.Pressed += OnRebuildPressed;
        _toolbar.AddChild(_rebuildButton);

        _bakeButton = new Button { Text = "Bake + Save" };
        _bakeButton.Pressed += OnBakePressed;
        _toolbar.AddChild(_bakeButton);

        _exportButton = new Button { Text = "Export C#" };
        _exportButton.Pressed += OnExportPressed;
        _toolbar.AddChild(_exportButton);

        _toolbar.AddChild(new VSeparator());

        _statusLabel = new Label
        {
            Text = "Select a FlowFieldBakeNode",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _toolbar.AddChild(_statusLabel);
    }

    private void OnRebuildPressed()
    {
        if (_activeNode == null)
            return;

        ApplyToolbarToNode();

        if (_activeNode.RebuildCostfieldFromTerrain(out string error))
        {
            SetStatus("Terrain costfield rebuilt", isError: false);
            GD.Print("[FlowFieldBakeEditorPlugin] Terrain costfield rebuilt.");
        }
        else
        {
            SetStatus(error, isError: true);
            GD.PrintErr($"[FlowFieldBakeEditorPlugin] {error}");
        }
    }

    private void OnBakePressed()
    {
        if (_activeNode == null)
            return;

        ApplyToolbarToNode();

        if (FlowFieldBaker.TryBakeAndSave(_activeNode, out string outputPath, out string error))
        {
            _activeNode.ShowFlowArrows = true;
            SyncToolbarFromNode();
            SetStatus($"Baked and saved: {outputPath}", isError: false);
            GD.Print($"[FlowFieldBakeEditorPlugin] Baked and saved -> {outputPath}");
            EditorInterface.Singleton.GetResourceFilesystem().Scan();
        }
        else
        {
            SetStatus(error, isError: true);
            GD.PrintErr($"[FlowFieldBakeEditorPlugin] {error}");
        }
    }

    private void OnExportPressed()
    {
        if (_activeNode == null)
            return;

        ApplyToolbarToNode();

        if (FlowFieldBakeCSharpExporter.TryExport(
                _activeNode,
                _activeNode.CSharpExportPath,
                out string absoluteOutputPath,
                out string error))
        {
            SetStatus($"Exported!", isError: false);
            GD.Print($"[FlowFieldBakeEditorPlugin] Exported C# -> {absoluteOutputPath}");

            var exportPath = (_activeNode.CSharpExportPath ?? string.Empty).Trim();
            if (exportPath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            {
                EditorInterface.Singleton.GetResourceFilesystem().Scan();
            }
        }
        else
        {
            SetStatus(error, isError: true);
            GD.PrintErr($"[FlowFieldBakeEditorPlugin] {error}");
        }
    }

    private void SyncToolbarFromNode()
    {
        if (_activeNode == null)
            return;

        _radiusSpin.Value = _activeNode.BrushRadiusCells;
        _costSpin.Value = _activeNode.BrushCostValue;
        _modeOption.Select((int)_activeNode.PaintMode);
        _showFlowArrowsToggle.ButtonPressed = _activeNode.ShowFlowArrows;
        _flowArrowTeamOption.Select((int)_activeNode.FlowArrowTeam);
    }

    private void ApplyToolbarToNode()
    {
        if (_activeNode == null)
            return;

        _activeNode.BrushRadiusCells = (int)_radiusSpin.Value;
        _activeNode.BrushCostValue = (int)_costSpin.Value;
        _activeNode.PaintMode = (FlowFieldBakeNode.BrushMode)_modeOption.GetSelectedId();
        _activeNode.ShowFlowArrows = _showFlowArrowsToggle.ButtonPressed;

        int flowArrowTeamId = _flowArrowTeamOption.GetSelectedId();
        if (flowArrowTeamId < 0)
            flowArrowTeamId = (int)FlowFieldBakeNode.FlowArrowTeamSelection.Team0;

        _activeNode.FlowArrowTeam = (FlowFieldBakeNode.FlowArrowTeamSelection)flowArrowTeamId;
    }

    private void UpdateToolbarVisibility()
    {
        if (_toolbar == null)
            return;

        _toolbar.Visible = _activeNode != null;
        if (_activeNode == null)
            _statusLabel.Text = "Select a FlowFieldBakeNode";
    }

    private bool TryGetTerrainIntersection(Camera3D viewportCamera, Vector2 mousePosition, out Vector3 hitPosition)
    {
        hitPosition = Vector3.Zero;

        Node terrainNode = _activeNode.ResolveTerrainNode();
        if (terrainNode == null)
            return false;

        Vector3 rayOrigin = viewportCamera.ProjectRayOrigin(mousePosition);
        Vector3 rayDirection = viewportCamera.ProjectRayNormal(mousePosition);
        Variant intersection = terrainNode.Call("get_intersection", rayOrigin, rayDirection, true);
        hitPosition = intersection.AsVector3();

        if (float.IsNaN(hitPosition.Y) || hitPosition.Z > 3.4e38f || float.IsInfinity(hitPosition.X))
            return false;

        return true;
    }

    private void SetStatus(string text, bool isError)
    {
        _statusLabel.Text = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        _statusLabel.Modulate = isError ? Colors.OrangeRed : Colors.White;
    }
}
#endif
