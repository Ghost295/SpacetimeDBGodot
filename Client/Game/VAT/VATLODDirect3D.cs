using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace SpacetimeDB.Game.VAT;

/// <summary>
/// LOD band levels for VAT animation speed control.
/// </summary>
public enum VATDirectLODBand : byte
{
    /// <summary>Full animation speed (1.0)</summary>
    Near = 0,
    /// <summary>Half animation speed (0.5)</summary>
    Mid = 1,
    /// <summary>Frozen or very slow animation (0.0)</summary>
    Far = 2
}

/// <summary>
/// Per-instance data for VAT direct rendering.
/// </summary>
public struct VATDirectInstance
{
    public Rid InstanceRid;
    public Transform3D Transform;
    public float AnimationOffset;
    public float StartFrame;
    public float EndFrame;
    public int TeamIndex;
    public float BaseScale;
    public int TrackIndex;
    public VATDirectLODBand CurrentLOD;
    public bool IsValid;
    public bool DataDirty;
}

/// <summary>
/// VAT LOD manager using RenderingServer directly for true per-unit LOD.
/// 
/// <para><b>Key Features:</b></para>
/// <list type="bullet">
/// <item>True per-unit LOD - each instance has its own material/LOD level</item>
/// <item>Direct RenderingServer usage - no MultiMesh limitations</item>
/// <item>Data texture for per-instance parameters (no instance uniform buffer limits)</item>
/// <item>Instance index encoded in transform scale (imperceptible variation)</item>
/// <item>Supports 10,000+ units with different models</item>
/// </list>
/// 
/// <para><b>Architecture:</b></para>
/// <list type="bullet">
/// <item>Each unit is a separate RenderingServer instance (Rid)</item>
/// <item>Instance data stored in shared data textures (animation data + scale)</item>
/// <item>Instance index encoded in scale: scale = base * (1 + index * 0.00001)</item>
/// <item>LOD controlled by swapping material per instance (Near/Mid/Far with different animation_speed)</item>
/// </list>
/// </summary>
[Tool, GlobalClass, Icon("res://Game/VAT/VAT3D.svg")]
public partial class VATLODDirect3D : Node3D
{
    #region Constants

    /// <summary>
    /// Scale factor for encoding instance index. 0.00001 supports 100k instances with &lt;1% scale variation.
    /// </summary>
    private const float IndexScaleFactor = 0.00001f;

    /// <summary>
    /// Data texture width - 256 pixels wide, so height = ceil(instanceCount / 256)
    /// </summary>
    private const int DataTextureWidth = 256;

    #endregion

    #region Exports

    /// <summary>
    /// Total number of VAT instances to create.
    /// </summary>
    [Export]
    public int InstanceCount { get; set; } = 1000;

    /// <summary>
    /// The VAT mesh resource.
    /// </summary>
    [Export]
    public Mesh VatMesh { get; set; }

    /// <summary>
    /// Material for near distance - full animation speed (animation_speed = 1.0)
    /// Must use vat_direct.gdshader
    /// </summary>
    [Export]
    public Material NearMaterial { get; set; }

    /// <summary>
    /// Material for mid distance - half animation speed (animation_speed = 0.5)
    /// Must use vat_direct.gdshader
    /// </summary>
    [Export]
    public Material MidMaterial { get; set; }

    /// <summary>
    /// Material for far distance - frozen animation (animation_speed = 0.0)
    /// Must use vat_direct.gdshader
    /// </summary>
    [Export]
    public Material FarMaterial { get; set; }

    /// <summary>
    /// Animation tracks: x = start frame, y = end frame.
    /// Use values from your Blender project.
    /// </summary>
    [Export]
    public Godot.Collections.Array<Vector2I> AnimationTracks { get; set; } = new();

    /// <summary>
    /// Random animation offset on/off.
    /// </summary>
    [Export]
    public bool RandAnimOffset { get; set; } = true;

    /// <summary>
    /// Distance threshold for Near band (below this = near)
    /// </summary>
    [Export]
    public float NearDistance { get; set; } = 50.0f;

    /// <summary>
    /// Distance threshold for Mid band (between near and mid = mid, above mid = far)
    /// </summary>
    [Export]
    public float MidDistance { get; set; } = 150.0f;

    /// <summary>
    /// Hysteresis margin to prevent LOD thrashing (default 5 units)
    /// </summary>
    [Export]
    public float HysteresisMargin { get; set; } = 5.0f;

    /// <summary>
    /// Optional camera reference. If null, uses viewport camera.
    /// </summary>
    [Export]
    public Camera3D CameraOverride { get; set; }

    /// <summary>
    /// Enable LOD updates. Disable for static scenes or debugging.
    /// </summary>
    [Export]
    public bool EnableLODUpdates { get; set; } = true;

    /// <summary>
    /// Number of instances to process per frame for LOD updates.
    /// Higher = faster convergence, Lower = better frame time distribution.
    /// Set to 0 to process all instances every frame.
    /// </summary>
    [Export]
    public int LODUpdatesPerFrame { get; set; } = 500;

    /// <summary>
    /// Debug visualization of LOD bands
    /// </summary>
    [Export]
    public bool DebugDrawLOD { get; set; } = false;

    #endregion

    #region Internal State

    private VATDirectInstance[] _instances;
    private Rid _scenarioRid;
    private Rid _meshRid;
    private Rid _nearMaterialRid;
    private Rid _midMaterialRid;
    private Rid _farMaterialRid;

    // Data texture for per-instance animation parameters (offset, start, end, team_index)
    private Image _dataTextureImage;
    private ImageTexture _dataTexture;
    
    // Scale texture for per-instance base scale (needed for index decoding)
    private Image _scaleTextureImage;
    private ImageTexture _scaleTexture;
    
    private int _dataTextureHeight;
    private bool _dataTextureDirty;

    // Squared distance thresholds with hysteresis
    private float _nearDistSq;
    private float _midDistSq;
    private float _nearDistSqLow;
    private float _midDistSqLow;

    // LOD counts for stats
    private int _nearCount;
    private int _midCount;
    private int _farCount;

    // Incremental LOD update state
    private int _lodUpdateIndex;

    private bool _initialized;
    private int _activeInstanceCount;

    // StringName for shader parameters (cached for performance)
    private static readonly StringName ParamInstanceDataTexture = "instance_data_texture";
    private static readonly StringName ParamInstanceScaleTexture = "instance_scale_texture";
    private static readonly StringName ParamDataTextureWidth = "data_texture_width";

    #endregion

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            return;

        Initialize();
    }

    public override void _ExitTree()
    {
        Cleanup();
    }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint() || !_initialized || !EnableLODUpdates)
            return;

        UpdateLOD();
        
        // Update data texture if any instance data changed
        if (_dataTextureDirty)
        {
            FlushDataTexture();
        }

        if (DebugDrawLOD)
        {
            DrawDebugLOD();
        }
    }

    #region Initialization

    private void Initialize()
    {
        if (VatMesh == null)
        {
            GD.PrintErr("VATLODDirect3D: VatMesh not set!");
            return;
        }

        if (AnimationTracks.Count == 0)
        {
            GD.PrintErr("VATLODDirect3D: No animation tracks defined!");
            return;
        }

        if (NearMaterial == null || MidMaterial == null || FarMaterial == null)
        {
            GD.PrintErr("VATLODDirect3D: LOD materials not set!");
            return;
        }

        // Get scenario from world
        var world = GetWorld3D();
        if (world == null)
        {
            GD.PrintErr("VATLODDirect3D: No World3D found!");
            return;
        }
        _scenarioRid = world.Scenario;

        // Cache mesh RID
        _meshRid = VatMesh.GetRid();

        // Cache material RIDs
        _nearMaterialRid = NearMaterial.GetRid();
        _midMaterialRid = MidMaterial.GetRid();
        _farMaterialRid = FarMaterial.GetRid();

        // Pre-calculate squared distances with hysteresis
        _nearDistSq = NearDistance * NearDistance;
        _midDistSq = MidDistance * MidDistance;
        _nearDistSqLow = (NearDistance - HysteresisMargin) * (NearDistance - HysteresisMargin);
        _midDistSqLow = (MidDistance - HysteresisMargin) * (MidDistance - HysteresisMargin);

        // Initialize data textures
        InitializeDataTextures();

        // Allocate instance array
        _instances = new VATDirectInstance[InstanceCount];

        GD.Print($"VATLODDirect3D: Initialized with capacity for {InstanceCount} instances, data texture {DataTextureWidth}x{_dataTextureHeight}");

        _initialized = true;
    }

    private void InitializeDataTextures()
    {
        // Calculate texture dimensions
        _dataTextureHeight = (InstanceCount + DataTextureWidth - 1) / DataTextureWidth;
        
        // Create image for animation data (offset, start, end, team_index)
        _dataTextureImage = Image.CreateEmpty(DataTextureWidth, _dataTextureHeight, false, Image.Format.Rgbaf);
        _dataTextureImage.Fill(new Color(0, 0, 1, 0)); // Default: offset=0, start=0, end=1, team_index=0
        _dataTexture = ImageTexture.CreateFromImage(_dataTextureImage);
        
        // Create image for scale data (base_scale per instance)
        _scaleTextureImage = Image.CreateEmpty(DataTextureWidth, _dataTextureHeight, false, Image.Format.Rf);
        _scaleTextureImage.Fill(new Color(1, 0, 0, 0)); // Default: base_scale=1.0
        _scaleTexture = ImageTexture.CreateFromImage(_scaleTextureImage);
        
        // Set textures on all materials
        SetMaterialTextures(NearMaterial);
        SetMaterialTextures(MidMaterial);
        SetMaterialTextures(FarMaterial);
    }

    private void SetMaterialTextures(Material material)
    {
        if (material is ShaderMaterial shaderMat)
        {
            shaderMat.SetShaderParameter(ParamInstanceDataTexture, _dataTexture);
            shaderMat.SetShaderParameter(ParamInstanceScaleTexture, _scaleTexture);
            shaderMat.SetShaderParameter(ParamDataTextureWidth, DataTextureWidth);
        }
    }

    #endregion

    #region Data Texture Management

    /// <summary>
    /// Updates the data texture pixel for a specific instance.
    /// </summary>
    private void UpdateInstanceDataPixel(int index, float offset, float startFrame, float endFrame, int teamIndex, float baseScale)
    {
        int x = index % DataTextureWidth;
        int y = index / DataTextureWidth;
        
        // Store animation data as RGBA: offset, start_frame, end_frame, team_index
        _dataTextureImage.SetPixel(x, y, new Color(offset, startFrame, endFrame, teamIndex));
        
        // Store base scale in separate texture (R channel only)
        _scaleTextureImage.SetPixel(x, y, new Color(baseScale, 0, 0, 0));
        
        _dataTextureDirty = true;
    }

    /// <summary>
    /// Flushes all pending data texture changes to the GPU.
    /// </summary>
    private void FlushDataTexture()
    {
        if (!_dataTextureDirty)
            return;

        // Update both textures from images
        _dataTexture.Update(_dataTextureImage);
        _scaleTexture.Update(_scaleTextureImage);
        _dataTextureDirty = false;
    }

    /// <summary>
    /// Encodes the instance index into a transform by slightly modifying the scale.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Transform3D EncodeIndexInTransform(Transform3D transform, int index, float baseScale)
    {
        // Encode index in scale: actual_scale = base_scale * (1 + index * INDEX_SCALE_FACTOR)
        float encodedScale = baseScale * (1.0f + index * IndexScaleFactor);
        
        // Apply encoded scale uniformly
        var scaledBasis = transform.Basis.Scaled(new Vector3(encodedScale, encodedScale, encodedScale) / baseScale);
        return new Transform3D(scaledBasis, transform.Origin);
    }

    #endregion

    #region Instance Creation

    /// <summary>
    /// Creates a single VAT instance at the specified index with given transform.
    /// </summary>
    public void CreateInstance(int index, Transform3D transform, int trackIndex = 0)
    {
        if (!_initialized || index < 0 || index >= InstanceCount)
            return;

        var track = AnimationTracks[trackIndex % AnimationTracks.Count];
        float animOffset = RandAnimOffset ? GD.Randf() : 0.0f;
        
        // Get the base scale from the original transform
        float baseScale = transform.Basis.Scale.X; // Assuming uniform scale
        if (baseScale < 0.001f) baseScale = 1.0f;

        // Encode instance index in transform
        var encodedTransform = EncodeIndexInTransform(transform, index, baseScale);

        // Create RenderingServer instance
        var instanceRid = RenderingServer.InstanceCreate();

        // Works but ideally we would be able to see all units
        // TODO: Make this a graphic setting?
        // RenderingServer.InstanceGeometrySetVisibilityRange(
        //     instanceRid, 
        //     0f,      // min distance (0 = always visible when close)
        //     500f,    // max distance (cull beyond this)
        //     0f,      // margin min (fade margin)
        //     10f,     // margin max (fade margin for smooth pop)
        //     RenderingServer.VisibilityRangeFadeMode.Disabled  // or Self for fade
        // );
        
        RenderingServer.InstanceSetBase(instanceRid, _meshRid);
        RenderingServer.InstanceSetScenario(instanceRid, _scenarioRid);
        RenderingServer.InstanceSetTransform(instanceRid, encodedTransform);

        // Set initial material (Near)
        RenderingServer.InstanceGeometrySetMaterialOverride(instanceRid, _nearMaterialRid);
        
        // Update data texture with instance parameters (including base scale)
        UpdateInstanceDataPixel(index, animOffset, track.X, track.Y, 0, baseScale);

        // Store instance data
        _instances[index] = new VATDirectInstance
        {
            InstanceRid = instanceRid,
            Transform = transform, // Store original transform (without index encoding)
            AnimationOffset = animOffset,
            StartFrame = track.X,
            EndFrame = track.Y,
            TeamIndex = 0,
            BaseScale = baseScale,
            TrackIndex = trackIndex,
            CurrentLOD = VATDirectLODBand.Near,
            IsValid = true,
            DataDirty = false
        };
        
        _activeInstanceCount++;
        _nearCount++;
    }

    /// <summary>
    /// Creates all instances at once using a position generation function.
    /// </summary>
    public void CreateAllInstances(Func<int, (Vector3 position, float scale, int trackIndex)> positionGenerator)
    {
        if (!_initialized)
        {
            GD.PrintErr("VATLODDirect3D: Not initialized. Call after _Ready().");
            return;
        }

        for (int i = 0; i < InstanceCount; i++)
        {
            var (pos, scale, track) = positionGenerator(i);
            var transform = new Transform3D(Basis.Identity.Scaled(Vector3.One * scale), pos);
            CreateInstance(i, transform, track);
        }

        // Flush data texture after creating all instances
        FlushDataTexture();

        GD.Print($"VATLODDirect3D: Created {_activeInstanceCount} instances");
    }

    /// <summary>
    /// Destroys a specific instance.
    /// </summary>
    public void DestroyInstance(int index)
    {
        if (!_initialized || index < 0 || index >= InstanceCount)
            return;

        ref var inst = ref _instances[index];
        if (!inst.IsValid)
            return;

        // Update LOD counts
        switch (inst.CurrentLOD)
        {
            case VATDirectLODBand.Near: _nearCount--; break;
            case VATDirectLODBand.Mid: _midCount--; break;
            case VATDirectLODBand.Far: _farCount--; break;
        }

        // Free RenderingServer instance
        RenderingServer.FreeRid(inst.InstanceRid);

        // Clear data in texture (set team index to 0, scale to 0)
        UpdateInstanceDataPixel(index, 0, 0, 1, 0, 0);

        inst.IsValid = false;
        _activeInstanceCount--;
    }

    #endregion

    #region LOD Updates

    private void UpdateLOD()
    {
        var camera = CameraOverride ?? GetViewport().GetCamera3D();
        if (camera == null)
            return;

        var camPos = camera.GlobalPosition;

        // Determine how many instances to process this frame
        int processCount = LODUpdatesPerFrame > 0 
            ? Math.Min(LODUpdatesPerFrame, _activeInstanceCount)
            : _activeInstanceCount;

        int processed = 0;
        int startIndex = _lodUpdateIndex;

        // Process instances incrementally
        while (processed < processCount)
        {
            if (_lodUpdateIndex >= InstanceCount)
                _lodUpdateIndex = 0;

            ref var inst = ref _instances[_lodUpdateIndex];
            if (inst.IsValid)
            {
                UpdateInstanceLOD(ref inst, camPos);
                processed++;
            }

            _lodUpdateIndex++;

            // Prevent infinite loop if all instances are invalid
            if (_lodUpdateIndex == startIndex)
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateInstanceLOD(ref VATDirectInstance inst, Vector3 camPos)
    {
        float distSq = (inst.Transform.Origin - camPos).LengthSquared();
        var newLOD = DetermineLODWithHysteresis(distSq, inst.CurrentLOD);

        if (newLOD != inst.CurrentLOD)
        {
            SetInstanceLOD(ref inst, newLOD);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private VATDirectLODBand DetermineLODWithHysteresis(float distSq, VATDirectLODBand currentLOD)
    {
        // Use hysteresis to prevent thrashing
        switch (currentLOD)
        {
            case VATDirectLODBand.Near:
                if (distSq >= _nearDistSq)
                    return VATDirectLODBand.Mid;
                return VATDirectLODBand.Near;

            case VATDirectLODBand.Mid:
                if (distSq < _nearDistSqLow)
                    return VATDirectLODBand.Near;
                if (distSq >= _midDistSq)
                    return VATDirectLODBand.Far;
                return VATDirectLODBand.Mid;

            case VATDirectLODBand.Far:
                if (distSq < _midDistSqLow)
                    return VATDirectLODBand.Mid;
                return VATDirectLODBand.Far;

            default:
                return VATDirectLODBand.Near;
        }
    }

    private void SetInstanceLOD(ref VATDirectInstance inst, VATDirectLODBand newLOD)
    {
        // Update LOD counts
        switch (inst.CurrentLOD)
        {
            case VATDirectLODBand.Near: _nearCount--; break;
            case VATDirectLODBand.Mid: _midCount--; break;
            case VATDirectLODBand.Far: _farCount--; break;
        }

        switch (newLOD)
        {
            case VATDirectLODBand.Near: _nearCount++; break;
            case VATDirectLODBand.Mid: _midCount++; break;
            case VATDirectLODBand.Far: _farCount++; break;
        }

        // Set material based on LOD
        var materialRid = newLOD switch
        {
            VATDirectLODBand.Near => _nearMaterialRid,
            VATDirectLODBand.Mid => _midMaterialRid,
            VATDirectLODBand.Far => _farMaterialRid,
            _ => _nearMaterialRid
        };

        RenderingServer.InstanceGeometrySetMaterialOverride(inst.InstanceRid, materialRid);
        inst.CurrentLOD = newLOD;
    }

    #endregion

    #region Instance Management

    /// <summary>
    /// Updates an instance's transform.
    /// </summary>
    public void SetInstanceTransform(int index, Transform3D transform)
    {
        if (!_initialized || index < 0 || index >= InstanceCount)
            return;

        ref var inst = ref _instances[index];
        if (!inst.IsValid)
            return;

        // Update base scale
        float baseScale = transform.Basis.Scale.X;
        if (baseScale < 0.001f) baseScale = 1.0f;
        inst.BaseScale = baseScale;

        inst.Transform = transform;
        
        // Re-encode index in new transform
        var encodedTransform = EncodeIndexInTransform(transform, index, baseScale);
        RenderingServer.InstanceSetTransform(inst.InstanceRid, encodedTransform);
        
        // Update scale in texture
        UpdateInstanceDataPixel(index, inst.AnimationOffset, inst.StartFrame, inst.EndFrame, inst.TeamIndex, baseScale);
    }

    /// <summary>
    /// Updates an instance's animation track.
    /// </summary>
    public void SetInstanceTrack(int index, int trackIndex)
    {
        if (!_initialized || index < 0 || index >= InstanceCount)
            return;

        ref var inst = ref _instances[index];
        if (!inst.IsValid)
            return;

        var track = AnimationTracks[trackIndex % AnimationTracks.Count];
        inst.StartFrame = track.X;
        inst.EndFrame = track.Y;
        inst.TrackIndex = trackIndex;

        // Update data texture
        UpdateInstanceDataPixel(index, inst.AnimationOffset, inst.StartFrame, inst.EndFrame, inst.TeamIndex, inst.BaseScale);
    }

    /// <summary>
    /// Updates an instance's animation offset (0..1) for looping playback.
    /// This controls the phase within the current track loop.
    /// For one-shot animations, use <see cref="PlayOneShotTrack"/> instead.
    /// </summary>
    public void SetInstanceAnimationOffset(int index, float animationOffset)
    {
        if (!_initialized || index < 0 || index >= InstanceCount)
            return;

        ref var inst = ref _instances[index];
        if (!inst.IsValid)
            return;

        // Clamp to valid loop offset range (0..1)
        inst.AnimationOffset = Mathf.Clamp(animationOffset, 0.0f, 1.0f);

        // Update data texture
        UpdateInstanceDataPixel(index, inst.AnimationOffset, inst.StartFrame, inst.EndFrame, inst.TeamIndex, inst.BaseScale);
    }

    /// <summary>
    /// Plays a one-shot animation track starting from the beginning.
    /// The animation will play once and clamp at the end frame.
    /// The unit's saved loop offset is preserved for when it returns to looping playback.
    /// </summary>
    /// <param name="index">Instance index.</param>
    /// <param name="trackIndex">Animation track to play.</param>
    /// <param name="startTimeMs">Start time in milliseconds (use Time.GetTicksMsec()).</param>
    /// <param name="savedLoopOffset">The loop offset to preserve (0..1), typically the current AnimationOffset.</param>
    public void PlayOneShotTrack(int index, int trackIndex, ulong startTimeMs, float savedLoopOffset)
    {
        if (!_initialized || index < 0 || index >= InstanceCount)
            return;

        ref var inst = ref _instances[index];
        if (!inst.IsValid)
            return;

        // Set the track frames
        var track = AnimationTracks[trackIndex % AnimationTracks.Count];
        inst.StartFrame = track.X;
        inst.EndFrame = track.Y;
        inst.TrackIndex = trackIndex;

        // Clamp saved offset to valid range
        savedLoopOffset = Mathf.Clamp(savedLoopOffset, 0.0f, 0.999f);

        // Pack one-shot encoding: packed = -(start_ms + saved_offset)
        // Shader detects one-shot when packed < 0
        float packed = -((float)startTimeMs + savedLoopOffset);
        inst.AnimationOffset = packed;

        // Update data texture
        UpdateInstanceDataPixel(index, inst.AnimationOffset, inst.StartFrame, inst.EndFrame, inst.TeamIndex, inst.BaseScale);
    }

    /// <summary>
    /// Gets the saved loop offset for an instance (useful before starting a one-shot).
    /// </summary>
    public float GetInstanceAnimationOffset(int index)
    {
        if (!_initialized || index < 0 || index >= InstanceCount)
            return 0.0f;

        ref var inst = ref _instances[index];
        if (!inst.IsValid)
            return 0.0f;

        // If currently playing one-shot (negative), extract saved offset from packed value
        if (inst.AnimationOffset < 0)
        {
            return Mathf.PosMod(Mathf.Abs(inst.AnimationOffset), 1.0f);
        }

        return inst.AnimationOffset;
    }

    /// <summary>
    /// Sets instance team index.
    /// </summary>
    public void SetInstanceTeamIndex(int index, int teamIndex)
    {
        if (!_initialized || index < 0 || index >= InstanceCount)
            return;

        ref var inst = ref _instances[index];
        if (!inst.IsValid)
            return;

        inst.TeamIndex = teamIndex;

        // Update data texture
        UpdateInstanceDataPixel(index, inst.AnimationOffset, inst.StartFrame, inst.EndFrame, inst.TeamIndex, inst.BaseScale);
    }

    /// <summary>
    /// Sets instance visibility.
    /// </summary>
    public void SetInstanceVisible(int index, bool visible)
    {
        if (!_initialized || index < 0 || index >= InstanceCount)
            return;

        ref var inst = ref _instances[index];
        if (!inst.IsValid)
            return;

        RenderingServer.InstanceSetVisible(inst.InstanceRid, visible);
    }

    /// <summary>
    /// Gets the current LOD band for an instance.
    /// </summary>
    public VATDirectLODBand GetInstanceLODBand(int index)
    {
        if (!_initialized || index < 0 || index >= InstanceCount)
            return VATDirectLODBand.Near;

        ref var inst = ref _instances[index];
        return inst.IsValid ? inst.CurrentLOD : VATDirectLODBand.Near;
    }

    /// <summary>
    /// Gets the position of an instance.
    /// </summary>
    public Vector3 GetInstancePosition(int index)
    {
        if (!_initialized || index < 0 || index >= InstanceCount)
            return Vector3.Zero;

        ref var inst = ref _instances[index];
        return inst.IsValid ? inst.Transform.Origin : Vector3.Zero;
    }

    /// <summary>
    /// Get animation start/end frames from track_number.
    /// </summary>
    public Vector2I GetStartEndFramesFromTrackNumber(int trackNumber)
    {
        return AnimationTracks[trackNumber % AnimationTracks.Count];
    }

    /// <summary>
    /// Gets instance counts per LOD band for debugging.
    /// </summary>
    public (int near, int mid, int far) GetLODCounts()
    {
        return (_nearCount, _midCount, _farCount);
    }

    /// <summary>
    /// Gets total active instance count.
    /// </summary>
    public int GetActiveInstanceCount()
    {
        return _activeInstanceCount;
    }

    #endregion

    #region Cleanup

    private void Cleanup()
    {
        if (_instances == null)
            return;

        // Free all RenderingServer instances
        for (int i = 0; i < _instances.Length; i++)
        {
            if (_instances[i].IsValid)
            {
                RenderingServer.FreeRid(_instances[i].InstanceRid);
                _instances[i].IsValid = false;
            }
        }

        _instances = null;
        _dataTextureImage = null;
        _dataTexture = null;
        _scaleTextureImage = null;
        _scaleTexture = null;
        _initialized = false;
        _activeInstanceCount = 0;
        _nearCount = 0;
        _midCount = 0;
        _farCount = 0;

        GD.Print("VATLODDirect3D: Cleanup complete");
    }

    #endregion

    #region Debug

    private void DrawDebugLOD()
    {
        if (!_initialized)
            return;

        // Draw LOD counts summary
        var counts = GetLODCounts();
        DebugDraw2D.SetText("lods", $"Direct VAT (Data Texture) - Near:{counts.near} Mid:{counts.mid} Far:{counts.far} Total:{_activeInstanceCount}");

        // Draw track index above each Near LOD instance
        for (int i = 0; i < _instances.Length; i++)
        {
            ref var inst = ref _instances[i];
            if (inst.IsValid && inst.CurrentLOD == VATDirectLODBand.Near)
            {
                // Draw text above the instance's head (offset up by 2 units)
                Vector3 textPos = inst.Transform.Origin + Vector3.Up * 3.0f;
                DebugDraw3D.DrawText(textPos, $"{inst.TrackIndex}", 12, Colors.Yellow);
            }
        }
    }

    #endregion

    #region Configuration Warnings

    public override string[] _GetConfigurationWarnings()
    {
        var warnings = new List<string>();

        // Required resources
        if (VatMesh == null)
            warnings.Add("VatMesh not set - assign the VAT mesh from your Blender export");
        if (NearMaterial == null)
            warnings.Add("NearMaterial not set - use vat_direct_near.tres or create a material with animation_speed=1.0");
        if (MidMaterial == null)
            warnings.Add("MidMaterial not set - use vat_direct_mid.tres or create a material with animation_speed=0.5");
        if (FarMaterial == null)
            warnings.Add("FarMaterial not set - use vat_direct_far.tres or create a material with animation_speed=0.0");
        if (AnimationTracks.Count == 0)
            warnings.Add("No animation tracks defined - add Vector2i(startFrame, endFrame) entries");

        // LOD configuration warnings
        if (NearDistance <= 0)
            warnings.Add("NearDistance must be positive");
        if (MidDistance <= NearDistance)
            warnings.Add("MidDistance should be greater than NearDistance");
        if (HysteresisMargin >= NearDistance)
            warnings.Add("HysteresisMargin should be much smaller than NearDistance");

        // Performance warnings
        if (InstanceCount > 65536)
            warnings.Add($"Very high instance count ({InstanceCount}). Data texture supports up to ~65k instances with 256-wide texture.");
        if (LODUpdatesPerFrame > 0 && LODUpdatesPerFrame < 100 && InstanceCount > 5000)
            warnings.Add($"LODUpdatesPerFrame ({LODUpdatesPerFrame}) is low for {InstanceCount} instances. Consider increasing for faster LOD convergence.");

        return warnings.ToArray();
    }

    #endregion
}
