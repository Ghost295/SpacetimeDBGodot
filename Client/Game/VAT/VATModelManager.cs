using System;
using System.Collections.Generic;
using Godot;

namespace SpacetimeDB.Game.VAT;

/// <summary>
/// Handle representing a spawned VAT instance within the manager.
/// </summary>
public readonly struct VATInstanceHandle
{
    /// <summary>
    /// The VATModel this instance belongs to.
    /// </summary>
    public readonly VATModel Model;
    
    /// <summary>
    /// The index within the VATLODDirect3D node.
    /// </summary>
    public readonly int InstanceIndex;
    
    /// <summary>
    /// Whether this handle is valid.
    /// </summary>
    public readonly bool IsValid;

    public VATInstanceHandle(VATModel model, int instanceIndex)
    {
        Model = model;
        InstanceIndex = instanceIndex;
        IsValid = model != null && instanceIndex >= 0;
    }

    public static readonly VATInstanceHandle Invalid = new(null, -1);
}

/// <summary>
/// High-level manager for spawning and managing VAT-enabled meshes.
/// Automatically creates and manages VATLODDirect3D nodes for each unique VATModel.
/// 
/// <para><b>Usage:</b></para>
/// <code>
/// // Get or create the manager
/// var manager = VATModelManager.GetOrCreate(this);
/// 
/// // Spawn instances
/// var handle = manager.SpawnInstance(vatModel, position, scale, trackIndex);
/// 
/// // Update instance
/// manager.SetInstanceTransform(handle, newTransform);
/// 
/// // Destroy instance
/// manager.DestroyInstance(handle);
/// </code>
/// </summary>
[Tool, GlobalClass]
public partial class VATModelManager : Node3D
{
    /// <summary>
    /// Default LOD configuration used when spawning models without explicit config.
    /// </summary>
    [Export]
    public VATLODConfig DefaultLODConfig { get; set; }

    /// <summary>
    /// Initial capacity for instances per model.
    /// </summary>
    [Export]
    public int InitialInstanceCapacity { get; set; } = 1000;

    /// <summary>
    /// Whether to enable random animation offsets by default.
    /// </summary>
    [Export]
    public bool RandAnimOffset { get; set; } = true;

    /// <summary>
    /// Enable LOD updates for all managed nodes.
    /// </summary>
    [Export]
    public bool EnableLODUpdates { get; set; } = true;

    /// <summary>
    /// Number of instances to process per frame for LOD updates.
    /// </summary>
    [Export]
    public int LODUpdatesPerFrame { get; set; } = 500;

    /// <summary>
    /// Optional camera override for all managed nodes.
    /// </summary>
    [Export]
    public Camera3D CameraOverride { get; set; }

    /// <summary>
    /// Debug visualization of LOD bands.
    /// </summary>
    [Export]
    public bool DebugDrawLOD { get; set; } = false;

    /// <summary>
    /// Team color palette used by VAT direct shader. Team index selects a palette entry.
    /// </summary>
    [Export]
    public Godot.Collections.Array<Color> TeamColorPalette { get; set; } =
        new Godot.Collections.Array<Color> { Colors.Blue, Colors.Red };
    
    #region Internal State

    private readonly Dictionary<VATModel, VATLODDirect3D> _modelNodes = new();
    private readonly Dictionary<VATModel, Queue<int>> _freeIndices = new();
    private Shader _vatShader;
    private bool _initialized;

    private static readonly StringName ParamOffsetMap = "offset_map";
    private static readonly StringName ParamNormalMap = "normal_map";
    private static readonly StringName ParamTextureAlbedo = "texture_albedo";
    private static readonly StringName ParamFPS = "fps";
    private static readonly StringName ParamAnimationSpeed = "animation_speed";
    private static readonly StringName ParamSkipInterpolation = "skip_interpolation";
    private static readonly StringName ParamSpecular = "specular";
    private static readonly StringName ParamMetallic = "metallic";
    private static readonly StringName ParamRoughness = "roughness";

    private const int TeamPaletteTextureWidth = 256;
    private static readonly StringName ParamTeamPaletteTexture = "team_palette_texture";
    private static readonly StringName ParamTeamPaletteCount = "team_palette_count";

    private Image _teamPaletteImage;
    private ImageTexture _teamPaletteTexture;

    #endregion

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            return;

        Initialize();
    }

    // TODO: This should use the var_direct_far_minimal.gdshader for far units to maximize performance
    private void Initialize()
    {
        if (_initialized)
            return;

        // Load the VAT shader
        _vatShader = GD.Load<Shader>("res://Game/VAT/shaders/vat_direct.gdshader");
        if (_vatShader == null)
        {
            GD.PrintErr("VATModelManager: Failed to load vat_direct.gdshader!");
            return;
        }

        // Create default config if not set
        DefaultLODConfig ??= VATLODConfig.CreateDefault();

        EnsureTeamPaletteTexture();
        ApplyTeamPaletteToAllMaterials();

        _initialized = true;
        GD.Print("VATModelManager: Initialized");
    }

    #region Static Factory

    /// <summary>
    /// Gets an existing VATModelManager or creates a new one as a child of the specified node.
    /// </summary>
    public static VATModelManager GetOrCreate(Node parent, string name = "VATModelManager")
    {
        // Look for existing manager
        foreach (var child in parent.GetChildren())
        {
            if (child is VATModelManager existing)
                return existing;
        }

        // Create new manager
        var manager = new VATModelManager { Name = name };
        parent.AddChild(manager);
        return manager;
    }

    #endregion

    #region Instance Management

    /// <summary>
    /// Spawns a new VAT instance.
    /// </summary>
    /// <param name="model">The VATModel to spawn.</param>
    /// <param name="position">World position.</param>
    /// <param name="scale">Uniform scale.</param>
    /// <param name="trackIndex">Animation track index.</param>
    /// <param name="lodConfig">Optional LOD configuration override.</param>
    /// <returns>Handle to the spawned instance.</returns>
    public VATInstanceHandle SpawnInstance(VATModel model, Vector3 position, float scale = 1.0f, int trackIndex = 0, VATLODConfig lodConfig = null)
    {
        if (!_initialized)
        {
            GD.PrintErr("VATModelManager: Not initialized!");
            return VATInstanceHandle.Invalid;
        }

        if (model == null || !model.IsValid())
        {
            GD.PrintErr("VATModelManager: Invalid VATModel!");
            return VATInstanceHandle.Invalid;
        }

        // Get or create the VATLODDirect3D node for this model
        var vatNode = GetOrCreateVATNode(model, lodConfig ?? DefaultLODConfig);
        if (vatNode == null)
            return VATInstanceHandle.Invalid;

        // Get a free index or allocate new
        int instanceIndex = GetFreeIndex(model, vatNode);
        if (instanceIndex < 0)
        {
            GD.PrintErr($"VATModelManager: No free instance slots for model {model}");
            return VATInstanceHandle.Invalid;
        }

        // Create the instance
        var transform = new Transform3D(Basis.Identity.Scaled(Vector3.One * scale), position);
        vatNode.CreateInstance(instanceIndex, transform, trackIndex);

        return new VATInstanceHandle(model, instanceIndex);
    }

    /// <summary>
    /// Spawns a new VAT instance with a custom transform.
    /// </summary>
    public VATInstanceHandle SpawnInstance(VATModel model, Transform3D transform, int trackIndex = 0, VATLODConfig lodConfig = null)
    {
        if (!_initialized)
        {
            GD.PrintErr("VATModelManager: Not initialized!");
            return VATInstanceHandle.Invalid;
        }

        if (model == null || !model.IsValid())
        {
            GD.PrintErr("VATModelManager: Invalid VATModel!");
            return VATInstanceHandle.Invalid;
        }

        var vatNode = GetOrCreateVATNode(model, lodConfig ?? DefaultLODConfig);
        if (vatNode == null)
            return VATInstanceHandle.Invalid;

        int instanceIndex = GetFreeIndex(model, vatNode);
        if (instanceIndex < 0)
        {
            GD.PrintErr($"VATModelManager: No free instance slots for model {model}");
            return VATInstanceHandle.Invalid;
        }

        vatNode.CreateInstance(instanceIndex, transform, trackIndex);

        return new VATInstanceHandle(model, instanceIndex);
    }

    /// <summary>
    /// Destroys a VAT instance.
    /// </summary>
    public void DestroyInstance(VATInstanceHandle handle)
    {
        if (!handle.IsValid)
            return;

        if (_modelNodes.TryGetValue(handle.Model, out var vatNode))
        {
            vatNode.DestroyInstance(handle.InstanceIndex);
            
            // Return the index to the free pool
            if (!_freeIndices.TryGetValue(handle.Model, out var freeQueue))
            {
                freeQueue = new Queue<int>();
                _freeIndices[handle.Model] = freeQueue;
            }
            freeQueue.Enqueue(handle.InstanceIndex);
        }
    }

    /// <summary>
    /// Destroys all active instances across all managed models.
    /// Useful when restarting a battle to avoid "ghost" instances persisting in the scene.
    /// </summary>
    public void DestroyAllInstances()
    {
        if (!_initialized)
            return;

        foreach (var kvp in _modelNodes)
        {
            VATModel model = kvp.Key;
            VATLODDirect3D vatNode = kvp.Value;
            if (vatNode == null)
                continue;

            int count = vatNode.InstanceCount;
            for (int i = 0; i < count; i++)
            {
                vatNode.DestroyInstance(i);
            }

            // Reset the free index pool back to a full, known-good state.
            if (!_freeIndices.TryGetValue(model, out var freeQueue))
            {
                freeQueue = new Queue<int>(count);
                _freeIndices[model] = freeQueue;
            }
            else
            {
                freeQueue.Clear();
            }

            for (int i = 0; i < count; i++)
            {
                freeQueue.Enqueue(i);
            }
        }

        GD.Print("VATModelManager: Destroyed all instances");
    }

    /// <summary>
    /// Sets the transform for an instance.
    /// </summary>
    public void SetInstanceTransform(VATInstanceHandle handle, Transform3D transform)
    {
        if (!handle.IsValid)
            return;

        if (_modelNodes.TryGetValue(handle.Model, out var vatNode))
        {
            vatNode.SetInstanceTransform(handle.InstanceIndex, transform);
        }
    }

    /// <summary>
    /// Sets the animation track for an instance.
    /// </summary>
    public void SetInstanceTrack(VATInstanceHandle handle, int trackIndex)
    {
        if (!handle.IsValid)
            return;

        if (_modelNodes.TryGetValue(handle.Model, out var vatNode))
        {
            vatNode.SetInstanceTrack(handle.InstanceIndex, trackIndex);
        }
    }

    /// <summary>
    /// Sets the animation offset (phase) for an instance (0..1).
    /// This controls the phase within the current track loop.
    /// For one-shot animations, use <see cref="PlayOneShotTrack"/> instead.
    /// </summary>
    public void SetInstanceAnimationOffset(VATInstanceHandle handle, float animationOffset)
    {
        if (!handle.IsValid)
        {
            GD.PrintErr($"VATModelManager: Invalid instance handle! Index: {handle.InstanceIndex}");
            return;
        }

        if (_modelNodes.TryGetValue(handle.Model, out var vatNode))
        {
            vatNode.SetInstanceAnimationOffset(handle.InstanceIndex, animationOffset);
        }
    }

    /// <summary>
    /// Plays a one-shot animation track starting from the beginning.
    /// The animation will play once and clamp at the end frame.
    /// The unit's saved loop offset is preserved for when it returns to looping playback.
    /// </summary>
    /// <param name="handle">Instance handle.</param>
    /// <param name="trackIndex">Animation track to play.</param>
    /// <param name="startTimeMs">Start time in milliseconds (use Time.GetTicksMsec()).</param>
    /// <param name="savedLoopOffset">The loop offset to preserve (0..1), typically from GetInstanceAnimationOffset.</param>
    public void PlayOneShotTrack(VATInstanceHandle handle, int trackIndex, ulong startTimeMs, float savedLoopOffset)
    {
        if (!handle.IsValid)
        {
            GD.PrintErr($"VATModelManager: Invalid instance handle! Index: {handle.InstanceIndex}");
            return;
        }

        if (_modelNodes.TryGetValue(handle.Model, out var vatNode))
        {
            vatNode.PlayOneShotTrack(handle.InstanceIndex, trackIndex, startTimeMs, savedLoopOffset);
        }
    }

    /// <summary>
    /// Gets the saved loop offset for an instance.
    /// Useful for preserving the offset before starting a one-shot animation.
    /// </summary>
    public float GetInstanceAnimationOffset(VATInstanceHandle handle)
    {
        if (!handle.IsValid)
            return 0.0f;

        if (_modelNodes.TryGetValue(handle.Model, out var vatNode))
        {
            return vatNode.GetInstanceAnimationOffset(handle.InstanceIndex);
        }

        return 0.0f;
    }

    /// <summary>
    /// Sets the team index for an instance (used by VAT direct team coloring).
    /// </summary>
    public void SetInstanceTeamIndex(VATInstanceHandle handle, int teamIndex)
    {
        if (!handle.IsValid)
            return;

        if (_modelNodes.TryGetValue(handle.Model, out var vatNode))
        {
            vatNode.SetInstanceTeamIndex(handle.InstanceIndex, teamIndex);
        }
    }

    /// <summary>
    /// Sets visibility for an instance.
    /// </summary>
    public void SetInstanceVisible(VATInstanceHandle handle, bool visible)
    {
        if (!handle.IsValid)
            return;

        if (_modelNodes.TryGetValue(handle.Model, out var vatNode))
        {
            vatNode.SetInstanceVisible(handle.InstanceIndex, visible);
        }
    }

    /// <summary>
    /// Gets the position of an instance.
    /// </summary>
    public Vector3 GetInstancePosition(VATInstanceHandle handle)
    {
        if (!handle.IsValid)
            return Vector3.Zero;

        if (_modelNodes.TryGetValue(handle.Model, out var vatNode))
        {
            return vatNode.GetInstancePosition(handle.InstanceIndex);
        }

        return Vector3.Zero;
    }

    /// <summary>
    /// Gets the LOD band for an instance.
    /// </summary>
    public VATDirectLODBand GetInstanceLODBand(VATInstanceHandle handle)
    {
        if (!handle.IsValid)
            return VATDirectLODBand.Near;

        if (_modelNodes.TryGetValue(handle.Model, out var vatNode))
        {
            return vatNode.GetInstanceLODBand(handle.InstanceIndex);
        }

        return VATDirectLODBand.Near;
    }

    #endregion

    #region Node Management

    private VATLODDirect3D GetOrCreateVATNode(VATModel model, VATLODConfig config)
    {
        if (_modelNodes.TryGetValue(model, out var existingNode))
            return existingNode;

        // Create new VATLODDirect3D node for this model
        var vatNode = new VATLODDirect3D
        {
            Name = $"VAT_{model.Mesh?.ResourcePath?.GetFile() ?? "Unknown"}",
            InstanceCount = InitialInstanceCapacity,
            VatMesh = model.Mesh,
            AnimationTracks = model.AnimationTracks,
            RandAnimOffset = RandAnimOffset,
            NearDistance = config.NearDistance,
            MidDistance = config.MidDistance,
            HysteresisMargin = config.HysteresisMargin,
            CameraOverride = CameraOverride,
            EnableLODUpdates = EnableLODUpdates,
            LODUpdatesPerFrame = LODUpdatesPerFrame,
            DebugDrawLOD = DebugDrawLOD
        };

        // Create LOD materials
        vatNode.NearMaterial = CreateLODMaterial(model.VatInfo, config, VATDirectLODBand.Near);
        vatNode.MidMaterial = CreateLODMaterial(model.VatInfo, config, VATDirectLODBand.Mid);
        vatNode.FarMaterial = CreateLODMaterial(model.VatInfo, config, VATDirectLODBand.Far);

        // Add to tree and register
        AddChild(vatNode);
        _modelNodes[model] = vatNode;
        _freeIndices[model] = new Queue<int>();

        // Pre-populate free indices
        for (int i = 0; i < InitialInstanceCapacity; i++)
        {
            _freeIndices[model].Enqueue(i);
        }

        GD.Print($"VATModelManager: Created VATLODDirect3D node for {model.Mesh?.ResourcePath}");

        return vatNode;
    }

    private ShaderMaterial CreateLODMaterial(VATInfo vatInfo, VATLODConfig config, VATDirectLODBand lod)
    {
        var material = new ShaderMaterial
        {
            Shader = _vatShader
        };

        // Set VAT textures from VATInfo
        material.SetShaderParameter(ParamOffsetMap, vatInfo.OffsetMap);
        material.SetShaderParameter(ParamNormalMap, vatInfo.NormalMap);
        material.SetShaderParameter(ParamTextureAlbedo, vatInfo.TextureAlbedo);

        // Set LOD-specific parameters
        material.SetShaderParameter(ParamFPS, config.FPS);
        material.SetShaderParameter(ParamSpecular, config.Specular);
        material.SetShaderParameter(ParamMetallic, config.Metallic);
        material.SetShaderParameter(ParamRoughness, config.Roughness);

        switch (lod)
        {
            case VATDirectLODBand.Near:
                material.SetShaderParameter(ParamAnimationSpeed, config.NearAnimationSpeed);
                material.SetShaderParameter(ParamSkipInterpolation, config.NearSkipInterpolation);
                break;
            case VATDirectLODBand.Mid:
                material.SetShaderParameter(ParamAnimationSpeed, config.MidAnimationSpeed);
                material.SetShaderParameter(ParamSkipInterpolation, config.MidSkipInterpolation);
                break;
            case VATDirectLODBand.Far:
                material.SetShaderParameter(ParamAnimationSpeed, config.FarAnimationSpeed);
                material.SetShaderParameter(ParamSkipInterpolation, config.FarSkipInterpolation);
                break;
        }

        ApplyTeamPaletteToMaterial(material);
        return material;
    }

    private int GetFreeIndex(VATModel model, VATLODDirect3D vatNode)
    {
        if (_freeIndices.TryGetValue(model, out var freeQueue) && freeQueue.Count > 0)
        {
            return freeQueue.Dequeue();
        }

        // No free indices - would need to expand capacity
        // For now, return -1 to indicate failure
        return -1;
    }

    #endregion

    #region Team Palette

    private void EnsureTeamPaletteTexture()
    {
        if (_teamPaletteTexture != null)
            return;

        _teamPaletteImage = Image.CreateEmpty(TeamPaletteTextureWidth, 1, false, Image.Format.Rgba8);
        _teamPaletteTexture = ImageTexture.CreateFromImage(_teamPaletteImage);
        UpdateTeamPaletteTexture();
    }

    private int GetTeamPaletteCount()
    {
        return Math.Max(1, TeamColorPalette?.Count ?? 0);
    }

    private void UpdateTeamPaletteTexture()
    {
        EnsureTeamPaletteTexture();

        int count = TeamColorPalette?.Count ?? 0;
        for (int i = 0; i < TeamPaletteTextureWidth; i++)
        {
            Color c = i < count ? TeamColorPalette[i] : Colors.White;
            c.A = 1.0f;
            _teamPaletteImage.SetPixel(i, 0, c);
        }

        _teamPaletteTexture.Update(_teamPaletteImage);
        ApplyTeamPaletteToAllMaterials();
    }

    private void ApplyTeamPaletteToMaterial(ShaderMaterial material)
    {
        EnsureTeamPaletteTexture();
        material.SetShaderParameter(ParamTeamPaletteTexture, _teamPaletteTexture);
        material.SetShaderParameter(ParamTeamPaletteCount, GetTeamPaletteCount());
    }

    private void ApplyTeamPaletteToAllMaterials()
    {
        if (_teamPaletteTexture == null)
            return;

        foreach (var node in _modelNodes.Values)
        {
            if (node.NearMaterial is ShaderMaterial nearMat)
                ApplyTeamPaletteToMaterial(nearMat);
            if (node.MidMaterial is ShaderMaterial midMat)
                ApplyTeamPaletteToMaterial(midMat);
            if (node.FarMaterial is ShaderMaterial farMat)
                ApplyTeamPaletteToMaterial(farMat);
        }
    }

    /// <summary>
    /// Replaces the palette and pushes updates to all managed VAT materials.
    /// </summary>
    public void SetTeamColorPalette(Godot.Collections.Array<Color> palette)
    {
        TeamColorPalette = palette ?? new Godot.Collections.Array<Color>();
        UpdateTeamPaletteTexture();
    }

    #endregion

    #region Statistics

    /// <summary>
    /// Gets the total number of active instances across all models.
    /// </summary>
    public int GetTotalActiveInstances()
    {
        int total = 0;
        foreach (var node in _modelNodes.Values)
        {
            total += node.GetActiveInstanceCount();
        }
        return total;
    }

    /// <summary>
    /// Gets LOD counts across all models.
    /// </summary>
    public (int near, int mid, int far) GetTotalLODCounts()
    {
        int near = 0, mid = 0, far = 0;
        foreach (var node in _modelNodes.Values)
        {
            var (n, m, f) = node.GetLODCounts();
            near += n;
            mid += m;
            far += f;
        }
        return (near, mid, far);
    }

    /// <summary>
    /// Gets the number of unique models currently managed.
    /// </summary>
    public int GetManagedModelCount()
    {
        return _modelNodes.Count;
    }

    /// <summary>
    /// Gets statistics for a specific model.
    /// </summary>
    public (int active, int near, int mid, int far) GetModelStats(VATModel model)
    {
        if (_modelNodes.TryGetValue(model, out var vatNode))
        {
            var (n, m, f) = vatNode.GetLODCounts();
            return (vatNode.GetActiveInstanceCount(), n, m, f);
        }
        return (0, 0, 0, 0);
    }

    #endregion

    #region Cleanup

    public override void _ExitTree()
    {
        Cleanup();
    }

    private void Cleanup()
    {
        // VATLODDirect3D nodes will clean themselves up when removed from tree
        _modelNodes.Clear();
        _freeIndices.Clear();
        _initialized = false;
        
        GD.Print("VATModelManager: Cleanup complete");
    }

    #endregion
}
