using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Framework.UI;

/// <summary>
/// Handles registration and inline JSON persistence for custom option definitions.
/// </summary>
internal sealed class OptionsCustomRegistry
{
    private readonly ResourceOptions _options;

    private readonly Dictionary<int, RegisteredSliderOption> _customSliderOptions = [];
    private readonly Dictionary<int, RegisteredDropdownOption> _customDropdownOptions = [];
    private readonly Dictionary<int, RegisteredLineEditOption> _customLineEditOptions = [];
    private readonly Dictionary<(OptionsTab Tab, string Label), int> _customOptionIds = [];
    private int _nextCustomOptionId;

    public OptionsCustomRegistry(ResourceOptions options)
    {
        _options = options;
    }

    public IEnumerable<RegisteredSliderOption> GetSliderOptions()
    {
        return _customSliderOptions.Values;
    }

    public IEnumerable<RegisteredDropdownOption> GetDropdownOptions()
    {
        return _customDropdownOptions.Values;
    }

    public IEnumerable<RegisteredLineEditOption> GetLineEditOptions()
    {
        return _customLineEditOptions.Values;
    }

    public RegisteredSliderOption AddSlider(SliderOptionDefinition option)
    {
        ArgumentNullException.ThrowIfNull(option);

        ValidateOptionLabel(option.Label, "Slider");
        ValidateSliderRange(option.MinValue, option.MaxValue, option.Step);

        int id = GetOrCreateOptionId(option.Tab, option.Label);
        string key = GetInlineSaveKey(option.Label);

        float minValue = (float)option.MinValue;
        float maxValue = (float)option.MaxValue;
        float defaultValue = Mathf.Clamp(option.DefaultValue, minValue, maxValue);

        float trackedValue = Mathf.Clamp(GetOrCreateSliderValue(key, defaultValue), minValue, maxValue);
        SetCustomSliderValue(key, trackedValue);
        option.SetValue(trackedValue);

        Func<float> getValue = () =>
        {
            float value = Mathf.Clamp(GetOrCreateSliderValue(key, defaultValue), minValue, maxValue);
            SetCustomSliderValue(key, value);
            return value;
        };

        Action<float> setValue = value =>
        {
            float clampedValue = Mathf.Clamp(value, minValue, maxValue);
            SetCustomSliderValue(key, clampedValue);
            option.SetValue(clampedValue);
        };

        RegisteredSliderOption slider = new(id, option, getValue, setValue);
        _customDropdownOptions.Remove(id);
        _customLineEditOptions.Remove(id);
        _customSliderOptions[id] = slider;
        return slider;
    }

    public RegisteredDropdownOption AddDropdown(DropdownOptionDefinition option)
    {
        ArgumentNullException.ThrowIfNull(option);

        ValidateOptionLabel(option.Label, "Dropdown");
        ValidateDropdownItems(option.Items);

        int id = GetOrCreateOptionId(option.Tab, option.Label);
        string key = GetInlineSaveKey(option.Label);

        int maxIndex = option.Items.Count - 1;
        int defaultValue = Mathf.Clamp(option.DefaultValue, 0, maxIndex);

        int trackedValue = Mathf.Clamp(GetOrCreateDropdownValue(key, defaultValue), 0, maxIndex);
        SetCustomDropdownValue(key, trackedValue);
        option.SetValue(trackedValue);

        Func<int> getValue = () =>
        {
            int value = Mathf.Clamp(GetOrCreateDropdownValue(key, defaultValue), 0, maxIndex);
            SetCustomDropdownValue(key, value);
            return value;
        };

        Action<int> setValue = value =>
        {
            int clampedValue = Mathf.Clamp(value, 0, maxIndex);
            SetCustomDropdownValue(key, clampedValue);
            option.SetValue(clampedValue);
        };

        RegisteredDropdownOption dropdown = new(id, option, getValue, setValue);
        _customSliderOptions.Remove(id);
        _customLineEditOptions.Remove(id);
        _customDropdownOptions[id] = dropdown;
        return dropdown;
    }

    public RegisteredLineEditOption AddLineEdit(LineEditOptionDefinition option)
    {
        ArgumentNullException.ThrowIfNull(option);

        ValidateOptionLabel(option.Label, "LineEdit");

        int id = GetOrCreateOptionId(option.Tab, option.Label);
        string key = GetInlineSaveKey(option.Label);
        string defaultValue = option.DefaultValue ?? string.Empty;

        string trackedValue = GetOrCreateLineEditValue(key, defaultValue);
        SetCustomLineEditValue(key, trackedValue);
        option.SetValue(trackedValue);

        Func<string> getValue = () => GetOrCreateLineEditValue(key, defaultValue);
        Action<string> setValue = value =>
        {
            string sanitized = value ?? string.Empty;
            SetCustomLineEditValue(key, sanitized);
            option.SetValue(sanitized);
        };

        RegisteredLineEditOption lineEdit = new(id, option, getValue, setValue);
        _customSliderOptions.Remove(id);
        _customDropdownOptions.Remove(id);
        _customLineEditOptions[id] = lineEdit;
        return lineEdit;
    }

    private int GetOrCreateOptionId(OptionsTab tab, string label)
    {
        (OptionsTab Tab, string Label) key = (tab, label);

        if (_customOptionIds.TryGetValue(key, out int id))
            return id;

        id = ++_nextCustomOptionId;
        _customOptionIds[key] = id;
        return id;
    }

    private static void ValidateOptionLabel(string label, string optionType)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException($"{optionType} label cannot be empty.");
    }

    private static void ValidateDropdownItems(IReadOnlyList<string> items)
    {
        if (items == null || items.Count == 0)
            throw new ArgumentException("Dropdown must define at least one item.");

        foreach (string item in items)
        {
            if (string.IsNullOrWhiteSpace(item))
                throw new ArgumentException("Dropdown items cannot be empty.");
        }
    }

    private static void ValidateSliderRange(double minValue, double maxValue, double step)
    {
        if (maxValue <= minValue)
            throw new ArgumentException("Slider max value must be greater than min value.");

        if (step <= 0)
            throw new ArgumentException("Slider step must be greater than 0.");
    }

    private static string GetInlineSaveKey(string label)
    {
        return ToPascalCaseKey(label);
    }

    private float GetOrCreateSliderValue(string key, float defaultValue)
    {
        Dictionary<string, JsonElement> values = _options.CustomOptionValues ??= [];

        if (values.TryGetValue(key, out JsonElement element))
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                if (element.TryGetSingle(out float number))
                    return number;

                if (element.TryGetDouble(out double numberDouble))
                    return (float)numberDouble;
            }
            else if (element.ValueKind == JsonValueKind.String
                && float.TryParse(element.GetString(), out float parsed))
            {
                return parsed;
            }
        }

        SetCustomSliderValue(key, defaultValue);
        return defaultValue;
    }

    private int GetOrCreateDropdownValue(string key, int defaultValue)
    {
        Dictionary<string, JsonElement> values = _options.CustomOptionValues ??= [];

        if (values.TryGetValue(key, out JsonElement element))
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                if (element.TryGetInt32(out int number))
                    return number;

                if (element.TryGetDouble(out double numberDouble))
                    return (int)numberDouble;
            }
            else if (element.ValueKind == JsonValueKind.String
                && int.TryParse(element.GetString(), out int parsed))
            {
                return parsed;
            }
        }

        SetCustomDropdownValue(key, defaultValue);
        return defaultValue;
    }

    private string GetOrCreateLineEditValue(string key, string defaultValue)
    {
        Dictionary<string, JsonElement> values = _options.CustomOptionValues ??= [];

        if (values.TryGetValue(key, out JsonElement element))
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => defaultValue ?? string.Empty
            };
        }

        string sanitized = defaultValue ?? string.Empty;
        SetCustomLineEditValue(key, sanitized);
        return sanitized;
    }

    private void SetCustomSliderValue(string key, float value)
    {
        Dictionary<string, JsonElement> values = _options.CustomOptionValues ??= [];
        values[key] = JsonSerializer.SerializeToElement(value);
    }

    private void SetCustomDropdownValue(string key, int value)
    {
        Dictionary<string, JsonElement> values = _options.CustomOptionValues ??= [];
        values[key] = JsonSerializer.SerializeToElement(value);
    }

    private void SetCustomLineEditValue(string key, string value)
    {
        Dictionary<string, JsonElement> values = _options.CustomOptionValues ??= [];
        values[key] = JsonSerializer.SerializeToElement(value ?? string.Empty);
    }

    private static string ToPascalCaseKey(string label)
    {
        string source = label ?? string.Empty;
        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;

        bool hasSeparator = false;
        foreach (char c in source)
        {
            if (!char.IsLetterOrDigit(c))
            {
                hasSeparator = true;
                break;
            }
        }

        if (!hasSeparator)
        {
            bool allUpper = true;
            foreach (char c in source)
            {
                if (char.IsLetter(c) && !char.IsUpper(c))
                {
                    allUpper = false;
                    break;
                }
            }

            if (allUpper)
            {
                string lowered = source.ToLowerInvariant();
                return char.ToUpperInvariant(lowered[0]) + lowered[1..];
            }

            return char.ToUpperInvariant(source[0]) + source[1..];
        }

        StringBuilder result = new();
        bool capitalizeNext = true;

        foreach (char c in source)
        {
            if (!char.IsLetterOrDigit(c))
            {
                capitalizeNext = true;
                continue;
            }

            if (capitalizeNext)
            {
                result.Append(char.ToUpperInvariant(c));
                capitalizeNext = false;
            }
            else
            {
                result.Append(char.ToLowerInvariant(c));
            }
        }

        return result.ToString();
    }
}
