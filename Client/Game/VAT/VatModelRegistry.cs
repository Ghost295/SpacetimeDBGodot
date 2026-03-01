using System;
using System.Collections.Generic;
using Godot;

namespace SpacetimeDB.Game.VAT;

/// <summary>
/// Deterministic VAT model registry that assigns stable integer ids.
/// Registration order must already be deterministic at call sites.
/// </summary>
public sealed class VatModelRegistry
{
    private const string LogPrefix = "[VatModelRegistry]";

    private readonly Dictionary<string, int> _idByModelKey = new(StringComparer.Ordinal);
    private readonly List<VATModel> _modelsById = new();
    private readonly List<string> _modelKeysById = new();

    public int Count => _modelsById.Count;
    public IReadOnlyList<VATModel> Models => _modelsById;

    public int Register(VATModel model, string debugSource = "")
    {
        if (model == null)
            return -1;

        string key = GetModelKey(model);
        if (_idByModelKey.TryGetValue(key, out int existingId))
            return existingId;

        int id = _modelsById.Count;
        _idByModelKey[key] = id;
        _modelsById.Add(model);
        _modelKeysById.Add(key);

        GD.Print($"{LogPrefix} Registered id={id} key='{key}' source='{debugSource}'.");
        return id;
    }

    public bool TryGetModelId(VATModel model, out int modelId)
    {
        if (model == null)
        {
            modelId = -1;
            return false;
        }

        string key = GetModelKey(model);
        return _idByModelKey.TryGetValue(key, out modelId);
    }

    public bool TryGetModelById(int modelId, out VATModel model)
    {
        if ((uint)modelId < (uint)_modelsById.Count)
        {
            model = _modelsById[modelId];
            return model != null;
        }

        model = null;
        return false;
    }

    public string GetModelKeyById(int modelId)
    {
        if ((uint)modelId < (uint)_modelKeysById.Count)
            return _modelKeysById[modelId];

        return string.Empty;
    }

    private static string GetModelKey(VATModel model)
    {
        string resourcePath = model.ResourcePath?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(resourcePath))
            return resourcePath;

        // Fallback for unsaved resources; authored assets should normally have stable paths.
        return $"__unsaved__:{model.GetInstanceId()}";
    }
}
