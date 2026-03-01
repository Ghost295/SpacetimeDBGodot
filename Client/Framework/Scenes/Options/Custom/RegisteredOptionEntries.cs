using System;

namespace Framework.UI;

// Internal runtime wrappers created by OptionsManager after registration.
// These hold stable IDs plus resolved getter/setter delegates used by the UI layer.
internal sealed class RegisteredSliderOption
{
    public RegisteredSliderOption(
        int id,
        SliderOptionDefinition definition,
        Func<float> getValue,
        Action<float> setValue)
    {
        Id = id;
        Definition = definition;
        GetValue = getValue;
        SetValue = setValue;
    }

    public int Id { get; }
    public SliderOptionDefinition Definition { get; }
    public Func<float> GetValue { get; }
    public Action<float> SetValue { get; }
}

internal sealed class RegisteredDropdownOption
{
    public RegisteredDropdownOption(
        int id,
        DropdownOptionDefinition definition,
        Func<int> getValue,
        Action<int> setValue)
    {
        Id = id;
        Definition = definition;
        GetValue = getValue;
        SetValue = setValue;
    }

    public int Id { get; }
    public DropdownOptionDefinition Definition { get; }
    public Func<int> GetValue { get; }
    public Action<int> SetValue { get; }
}

internal sealed class RegisteredLineEditOption
{
    public RegisteredLineEditOption(
        int id,
        LineEditOptionDefinition definition,
        Func<string> getValue,
        Action<string> setValue)
    {
        Id = id;
        Definition = definition;
        GetValue = getValue;
        SetValue = setValue;
    }

    public int Id { get; }
    public LineEditOptionDefinition Definition { get; }
    public Func<string> GetValue { get; }
    public Action<string> SetValue { get; }
}
