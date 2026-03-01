using Godot;
using System;
using System.Collections.Generic;

namespace SpacetimeDB.Game.VAT;

/// <summary>
/// Test scene for VATLODDirect3D - demonstrates true per-unit LOD using RenderingServer.
/// High-level API (UseHighLevelAPI = true): Uses VATModelManager with VATModel resources
/// </summary>
public partial class VATLODDirectTest : Node3D
{
	/// <summary>
	/// Size of each grid cell in world units. Adjust this to change spacing between instances.
	/// </summary>
	[Export]
	public float CellSize { get; set; } = 3.0f;
	
	/// <summary>
	/// Show debug visualization of LOD stats
	/// </summary>
	[Export]
	public bool ShowDebugLOD { get; set; } = false;
    
	/// <summary>
	/// VATModel resources to spawn when using high-level API.
	/// Multiple models can be spawned simultaneously.
	/// </summary>
	[Export]
	public Godot.Collections.Array<VATModel> VATModels { get; set; } = new();

	/// <summary>
	/// Optional custom LOD configuration for high-level API mode.
	/// If null, uses default configuration.
	/// </summary>
	[Export]
	public VATLODConfig LODConfig { get; set; }

	/// <summary>
	/// Number of instances to spawn per VATModel.
	/// </summary>
	[Export]
	public int InstancesPerModel { get; set; } = 1000;
	
	#region Internal State

	private VATLODDirect3D _vatDirect;
	private VATModelManager _modelManager;
	private List<VATInstanceHandle> _handles = new();

	#endregion

	public override void _Ready()
	{
		SetupHighLevelAPI();
	}

	#region High-Level API Setup

	private void SetupHighLevelAPI()
	{
		if (VATModels.Count == 0)
		{
			GD.PrintErr("VATLODDirectTest: No VATModels assigned for high-level API mode!");
			return;
		}

		// Create or get the model manager
		_modelManager = VATModelManager.GetOrCreate(this);
		_modelManager.DebugDrawLOD = ShowDebugLOD;
		_modelManager.InitialInstanceCapacity = InstancesPerModel;

		if (LODConfig != null)
		{
			_modelManager.DefaultLODConfig = LODConfig;
		}

		// Wait one frame to ensure everything is ready
		CallDeferred(nameof(SetupHighLevelInstances));
	}

	private void SetupHighLevelInstances()
	{
		// Filter to only valid models
		var validModels = new List<VATModel>();
		foreach (var model in VATModels)
		{
			if (model != null && model.IsValid())
			{
				validModels.Add(model);
			}
			else
			{
				GD.PrintErr("VATLODDirectTest: Skipping invalid VATModel");
			}
		}

		if (validModels.Count == 0)
		{
			GD.PrintErr("VATLODDirectTest: No valid VATModels to spawn!");
			return;
		}

		// Generate positions once
		List<Vector3> positions = GenerateGridPositions(InstancesPerModel);
		int totalInstances = 0;

		// Spawn instances, randomly picking a model for each position
		for (int i = 0; i < InstancesPerModel && i < positions.Count; i++)
		{
			// Randomly pick a model
			var model = validModels[(int)(GD.Randi() % validModels.Count)];
			int trackCount = model.AnimationTracks.Count;

			Vector3 pos = positions[i];
			int track = i % trackCount;

			var handle = _modelManager.SpawnInstance(model, pos, 1.0f, track);
			if (handle.IsValid)
			{
				_handles.Add(handle);
				totalInstances++;
			}
		}

		GD.Print($"VATLODDirectTest: High-level API setup complete with {totalInstances} total instances across {validModels.Count} models");
	}

	#endregion

	#region Position Generation

	/// <summary>
	/// Generates grid positions starting from center and expanding outward.
	/// </summary>
	private List<Vector3> GenerateGridPositions(int instanceCount)
	{
		if (instanceCount <= 0)
			return new List<Vector3>();

		int gridSize = Mathf.CeilToInt(Mathf.Sqrt(instanceCount));
		
		List<Vector3> positions = new List<Vector3>(gridSize * gridSize);

		for (int x = 0; x < gridSize; x++)
		{
			for (int z = 0; z < gridSize; z++)
			{
				float worldX = (x - gridSize / 2.0f) * CellSize;
				float worldZ = (z - gridSize / 2.0f) * CellSize;
				positions.Add(new Vector3(worldX, 0, worldZ));
			}
		}

		positions.Sort((a, b) =>
		{
			float distA = a.LengthSquared();
			float distB = b.LengthSquared();
			return distA.CompareTo(distB);
		});

		if (positions.Count > instanceCount)
		{
			positions.RemoveRange(instanceCount, positions.Count - instanceCount);
		}

		return positions;
	}

	#endregion

	public override void _Process(double delta)
	{
		_modelManager.DebugDrawLOD = ShowDebugLOD;
	}

	#region Configuration Warnings

	public override string[] _GetConfigurationWarnings()
	{
		var warnings = new List<string>();
        
		if (VATModels.Count == 0)
		{
			warnings.Add("High-level API mode is enabled but no VATModels are assigned.");
		}
		else
		{
			for (int i = 0; i < VATModels.Count; i++)
			{
				var model = VATModels[i];
				if (model == null)
				{
					warnings.Add($"VATModel at index {i} is null.");
				}
				else if (!model.IsValid())
				{
					warnings.Add($"VATModel at index {i} is invalid (missing mesh, VATInfo, or animation tracks).");
				}
			}
		}

		return warnings.ToArray();
	}

	#endregion
}
