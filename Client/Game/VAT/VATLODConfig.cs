using Godot;

namespace SpacetimeDB.Game.VAT;

/// <summary>
/// Configuration for LOD materials used by VATModelManager.
/// </summary>
[Tool, GlobalClass]
public partial class VATLODConfig : Resource
{
    /// <summary>
    /// FPS for animation playback.
    /// </summary>
    [Export]
    public float FPS { get; set; } = 24.0f;

    /// <summary>
    /// Near LOD animation speed (1.0 = full speed).
    /// </summary>
    [Export]
    public float NearAnimationSpeed { get; set; } = 1.0f;

    /// <summary>
    /// Mid LOD animation speed (0.5 = half speed).
    /// </summary>
    [Export]
    public float MidAnimationSpeed { get; set; } = 0.5f;

    /// <summary>
    /// Far LOD animation speed (0.0 = frozen).
    /// </summary>
    [Export]
    public float FarAnimationSpeed { get; set; } = 0.0f;

    /// <summary>
    /// Skip interpolation for near LOD.
    /// </summary>
    [Export]
    public bool NearSkipInterpolation { get; set; } = false;

    /// <summary>
    /// Skip interpolation for mid LOD.
    /// </summary>
    [Export]
    public bool MidSkipInterpolation { get; set; } = true;

    /// <summary>
    /// Skip interpolation for far LOD.
    /// </summary>
    [Export]
    public bool FarSkipInterpolation { get; set; } = true;

    /// <summary>
    /// Material specular value.
    /// </summary>
    [Export(PropertyHint.Range, "0,1")]
    public float Specular { get; set; } = 0.5f;

    /// <summary>
    /// Material metallic value.
    /// </summary>
    [Export(PropertyHint.Range, "0,1")]
    public float Metallic { get; set; } = 0.0f;

    /// <summary>
    /// Material roughness value.
    /// </summary>
    [Export(PropertyHint.Range, "0,1")]
    public float Roughness { get; set; } = 1.0f;

    /// <summary>
    /// Distance threshold for Near band (below this = near).
    /// </summary>
    [Export]
    public float NearDistance { get; set; } = 50.0f;

    /// <summary>
    /// Distance threshold for Mid band (between near and mid = mid, above mid = far).
    /// </summary>
    [Export]
    public float MidDistance { get; set; } = 150.0f;

    /// <summary>
    /// Hysteresis margin to prevent LOD thrashing.
    /// </summary>
    [Export]
    public float HysteresisMargin { get; set; } = 5.0f;

    /// <summary>
    /// Creates a default LOD configuration.
    /// </summary>
    public static VATLODConfig CreateDefault()
    {
        return new VATLODConfig();
    }
}


