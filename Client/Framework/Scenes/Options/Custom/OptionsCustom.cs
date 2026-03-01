using Godot;
using System;
using System.Collections.Generic;

namespace Framework.UI;

/// <summary>
/// Creates and manages UI controls for custom options registered via OptionsManager.
/// </summary>
public sealed class OptionsCustom : IDisposable
{
    private readonly Dictionary<int, IDisposable> _bindings = [];
    private readonly OptionsManager _optionsManager;
    private readonly OptionsNav _nav;

    public OptionsCustom(OptionsNav nav)
    {
        _nav = nav;
        _optionsManager = GameFramework.Options;

        List<CustomOptionDescriptor> options = [];

        foreach (RegisteredSliderOption slider in _optionsManager.GetSliderOptions())
        {
            options.Add(new CustomOptionDescriptor(slider));
        }

        foreach (RegisteredDropdownOption dropdown in _optionsManager.GetDropdownOptions())
        {
            options.Add(new CustomOptionDescriptor(dropdown));
        }

        foreach (RegisteredLineEditOption lineEdit in _optionsManager.GetLineEditOptions())
        {
            options.Add(new CustomOptionDescriptor(lineEdit));
        }

        options.Sort(SortDefinitions);

        foreach (CustomOptionDescriptor option in options)
        {
            AddOrReplaceOption(option);
        }

        _optionsManager.SliderOptionRegistered += OnSliderOptionRegistered;
        _optionsManager.DropdownOptionRegistered += OnDropdownOptionRegistered;
        _optionsManager.LineEditOptionRegistered += OnLineEditOptionRegistered;
    }

    public void Dispose()
    {
        _optionsManager.SliderOptionRegistered -= OnSliderOptionRegistered;
        _optionsManager.DropdownOptionRegistered -= OnDropdownOptionRegistered;
        _optionsManager.LineEditOptionRegistered -= OnLineEditOptionRegistered;

        foreach (IDisposable binding in _bindings.Values)
        {
            binding.Dispose();
        }

        _bindings.Clear();
        GC.SuppressFinalize(this);
    }

    private void OnSliderOptionRegistered(RegisteredSliderOption slider)
    {
        AddOrReplaceOption(new CustomOptionDescriptor(slider));
    }

    private void OnDropdownOptionRegistered(RegisteredDropdownOption dropdown)
    {
        AddOrReplaceOption(new CustomOptionDescriptor(dropdown));
    }

    private void OnLineEditOptionRegistered(RegisteredLineEditOption lineEdit)
    {
        AddOrReplaceOption(new CustomOptionDescriptor(lineEdit));
    }

    private void AddOrReplaceOption(CustomOptionDescriptor option)
    {
        if (!_nav.TryGetTabContainer(option.Tab, out VBoxContainer tabContainer))
            return;

        Button navButton = GetNavButton(option.Tab);
        if (navButton == null)
            return;

        // Re-registration replaces the existing control in-place.
        if (_bindings.TryGetValue(option.Id, out IDisposable existing))
        {
            existing.Dispose();
            _bindings.Remove(option.Id);
        }

        IDisposable binding = option.Type switch
        {
            CustomOptionType.Slider => CreateSliderBinding(tabContainer, navButton, option.Slider),
            CustomOptionType.Dropdown => CreateDropdownBinding(tabContainer, navButton, option.Dropdown),
            CustomOptionType.LineEdit => CreateLineEditBinding(tabContainer, navButton, option.LineEdit),
            _ => null
        };

        if (binding == null)
            return;

        _bindings.Add(option.Id, binding);
    }

    private static int SortDefinitions(CustomOptionDescriptor left, CustomOptionDescriptor right)
    {
        int tabComparison = left.Tab.CompareTo(right.Tab);
        if (tabComparison != 0)
            return tabComparison;

        int orderComparison = left.Order.CompareTo(right.Order);
        if (orderComparison != 0)
            return orderComparison;

        return left.Id.CompareTo(right.Id);
    }

    private static SliderBinding CreateSliderBinding(
        VBoxContainer tabContainer,
        Button navButton,
        RegisteredSliderOption sliderOption)
    {
        SliderOptionDefinition sliderDefinition = sliderOption.Definition;

        HBoxContainer row = new()
        {
            Name = $"CustomSlider_{sliderOption.Id}"
        };

        Label label = new()
        {
            Text = string.IsNullOrWhiteSpace(sliderDefinition.Label) ? $"SLIDER_{sliderOption.Id}" : sliderDefinition.Label,
            CustomMinimumSize = new Vector2(200, 0)
        };
        row.AddChild(label);

        HSlider slider = new()
        {
            CustomMinimumSize = new Vector2(250, 0),
            MinValue = sliderDefinition.MinValue,
            MaxValue = sliderDefinition.MaxValue,
            Step = sliderDefinition.Step
        };
        slider.FocusNeighborLeft = navButton.GetPath();
        row.AddChild(slider);

        tabContainer.AddChild(row);

        if (tabContainer.GetChildCount() == 1)
        {
            navButton.FocusNeighborRight = slider.GetPath();
        }

        float clampedValue = Mathf.Clamp(
            sliderOption.GetValue(),
            (float)sliderDefinition.MinValue,
            (float)sliderDefinition.MaxValue);

        sliderOption.SetValue(clampedValue);
        slider.Value = clampedValue;

        Godot.Range.ValueChangedEventHandler onValueChanged = v => sliderOption.SetValue((float)v);
        slider.ValueChanged += onValueChanged;

        return new SliderBinding(row, slider, onValueChanged);
    }

    private static DropdownBinding CreateDropdownBinding(
        VBoxContainer tabContainer,
        Button navButton,
        RegisteredDropdownOption dropdownOption)
    {
        DropdownOptionDefinition dropdownDefinition = dropdownOption.Definition;

        HBoxContainer row = new()
        {
            Name = $"CustomDropdown_{dropdownOption.Id}"
        };

        Label label = new()
        {
            Text = string.IsNullOrWhiteSpace(dropdownDefinition.Label) ? $"DROPDOWN_{dropdownOption.Id}" : dropdownDefinition.Label,
            CustomMinimumSize = new Vector2(200, 0)
        };
        row.AddChild(label);

        OptionButton dropdown = new()
        {
            CustomMinimumSize = new Vector2(250, 0)
        };

        for (int index = 0; index < dropdownDefinition.Items.Count; index++)
        {
            dropdown.AddItem(dropdownDefinition.Items[index], index);
        }

        dropdown.FocusNeighborLeft = navButton.GetPath();
        row.AddChild(dropdown);
        tabContainer.AddChild(row);

        if (tabContainer.GetChildCount() == 1)
        {
            navButton.FocusNeighborRight = dropdown.GetPath();
        }

        int maxIndex = dropdownDefinition.Items.Count - 1;
        int clampedValue = Mathf.Clamp(dropdownOption.GetValue(), 0, maxIndex);
        dropdownOption.SetValue(clampedValue);
        dropdown.Select(clampedValue);

        OptionButton.ItemSelectedEventHandler onItemSelected = index => dropdownOption.SetValue((int)index);
        dropdown.ItemSelected += onItemSelected;

        return new DropdownBinding(row, dropdown, onItemSelected);
    }

    private static LineEditBinding CreateLineEditBinding(
        VBoxContainer tabContainer,
        Button navButton,
        RegisteredLineEditOption lineEditOption)
    {
        LineEditOptionDefinition lineEditDefinition = lineEditOption.Definition;

        HBoxContainer row = new()
        {
            Name = $"CustomLineEdit_{lineEditOption.Id}"
        };

        Label label = new()
        {
            Text = string.IsNullOrWhiteSpace(lineEditDefinition.Label) ? $"LINE_EDIT_{lineEditOption.Id}" : lineEditDefinition.Label,
            CustomMinimumSize = new Vector2(200, 0)
        };
        row.AddChild(label);

        LineEdit lineEdit = new()
        {
            CustomMinimumSize = new Vector2(250, 0),
            PlaceholderText = lineEditDefinition.Placeholder
        };

        lineEdit.FocusNeighborLeft = navButton.GetPath();
        row.AddChild(lineEdit);
        tabContainer.AddChild(row);

        if (tabContainer.GetChildCount() == 1)
        {
            navButton.FocusNeighborRight = lineEdit.GetPath();
        }

        string value = lineEditOption.GetValue() ?? string.Empty;
        lineEditOption.SetValue(value);
        lineEdit.Text = value;

        LineEdit.TextChangedEventHandler onTextChanged = text => lineEditOption.SetValue(text ?? string.Empty);
        lineEdit.TextChanged += onTextChanged;

        return new LineEditBinding(row, lineEdit, onTextChanged);
    }

    private Button GetNavButton(OptionsTab tab)
    {
        return tab switch
        {
            OptionsTab.General => _nav.GeneralButton,
            OptionsTab.Gameplay => _nav.GameplayButton,
            OptionsTab.Display => _nav.DisplayButton,
            OptionsTab.Graphics => _nav.GraphicsButton,
            OptionsTab.Audio => _nav.AudioButton,
            OptionsTab.Input => _nav.InputButton,
            _ => null
        };
    }

    private sealed class SliderBinding : IDisposable
    {
        private readonly HBoxContainer _row;
        private readonly HSlider _slider;
        private readonly Godot.Range.ValueChangedEventHandler _onValueChanged;

        public SliderBinding(HBoxContainer row, HSlider slider, Godot.Range.ValueChangedEventHandler onValueChanged)
        {
            _row = row;
            _slider = slider;
            _onValueChanged = onValueChanged;
        }

        public void Dispose()
        {
            if (GodotObject.IsInstanceValid(_slider))
            {
                _slider.ValueChanged -= _onValueChanged;
            }

            if (GodotObject.IsInstanceValid(_row))
            {
                _row.QueueFree();
            }

            GC.SuppressFinalize(this);
        }
    }

    private sealed class DropdownBinding : IDisposable
    {
        private readonly HBoxContainer _row;
        private readonly OptionButton _dropdown;
        private readonly OptionButton.ItemSelectedEventHandler _onItemSelected;

        public DropdownBinding(
            HBoxContainer row,
            OptionButton dropdown,
            OptionButton.ItemSelectedEventHandler onItemSelected)
        {
            _row = row;
            _dropdown = dropdown;
            _onItemSelected = onItemSelected;
        }

        public void Dispose()
        {
            if (GodotObject.IsInstanceValid(_dropdown))
            {
                _dropdown.ItemSelected -= _onItemSelected;
            }

            if (GodotObject.IsInstanceValid(_row))
            {
                _row.QueueFree();
            }

            GC.SuppressFinalize(this);
        }
    }

    private sealed class LineEditBinding : IDisposable
    {
        private readonly HBoxContainer _row;
        private readonly LineEdit _lineEdit;
        private readonly LineEdit.TextChangedEventHandler _onTextChanged;

        public LineEditBinding(
            HBoxContainer row,
            LineEdit lineEdit,
            LineEdit.TextChangedEventHandler onTextChanged)
        {
            _row = row;
            _lineEdit = lineEdit;
            _onTextChanged = onTextChanged;
        }

        public void Dispose()
        {
            if (GodotObject.IsInstanceValid(_lineEdit))
            {
                _lineEdit.TextChanged -= _onTextChanged;
            }

            if (GodotObject.IsInstanceValid(_row))
            {
                _row.QueueFree();
            }

            GC.SuppressFinalize(this);
        }
    }

    private enum CustomOptionType
    {
        Slider,
        Dropdown,
        LineEdit
    }

    private readonly struct CustomOptionDescriptor
    {
        public CustomOptionDescriptor(RegisteredSliderOption slider)
        {
            Id = slider.Id;
            Tab = slider.Definition.Tab;
            Order = slider.Definition.Order;
            Slider = slider;
            Dropdown = null;
            LineEdit = null;
            Type = CustomOptionType.Slider;
        }

        public CustomOptionDescriptor(RegisteredDropdownOption dropdown)
        {
            Id = dropdown.Id;
            Tab = dropdown.Definition.Tab;
            Order = dropdown.Definition.Order;
            Slider = null;
            Dropdown = dropdown;
            LineEdit = null;
            Type = CustomOptionType.Dropdown;
        }

        public CustomOptionDescriptor(RegisteredLineEditOption lineEdit)
        {
            Id = lineEdit.Id;
            Tab = lineEdit.Definition.Tab;
            Order = lineEdit.Definition.Order;
            Slider = null;
            Dropdown = null;
            LineEdit = lineEdit;
            Type = CustomOptionType.LineEdit;
        }

        public int Id { get; }
        public OptionsTab Tab { get; }
        public int Order { get; }
        public RegisteredSliderOption Slider { get; }
        public RegisteredDropdownOption Dropdown { get; }
        public RegisteredLineEditOption LineEdit { get; }
        public CustomOptionType Type { get; }
    }
}
