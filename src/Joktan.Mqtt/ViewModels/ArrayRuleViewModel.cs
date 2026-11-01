using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Core.ViewModels;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt.ViewModels;

/// <summary>
/// One array size in the message settings editor.
/// </summary>
public partial class ArrayRuleViewModel : ViewModelBase
{
    public ArrayRuleViewModel()
    {
    }

    public ArrayRuleViewModel(ArrayRule rule)
    {
        Path = rule.Path;
        Items = rule.Min.ToString(CultureInfo.InvariantCulture);
        MaxItems = rule.IsFixed ? "" : rule.Max.ToString(CultureInfo.InvariantCulture);
    }

    [ObservableProperty]
    public partial string Path { get; set; } = "";

    /// <summary>The number of items, or the smallest number when <see cref="MaxItems"/> is set.</summary>
    [ObservableProperty]
    public partial string Items { get; set; } = "3";

    /// <summary>The largest number of items, or empty for always <see cref="Items"/>.</summary>
    [ObservableProperty]
    public partial string MaxItems { get; set; } = "";

    /// <exception cref="FormatException">A setting is missing or not a number.</exception>
    public ArrayRule ToRule()
    {
        var path = Path.Trim();
        if (path.Length == 0)
            throw new FormatException(Strings.ArrayRule_EveryArraySizeNeedsAnArray);

        var min = Whole(Items, Strings.ArrayRule_NumberOfItems);
        var max = MaxItems.Trim().Length == 0 ? min : Whole(MaxItems, Strings.ArrayRule_LargestNumberOfItems);
        var rule = new ArrayRule(path, min, max);
        if (MessageSettings.CheckArray(rule) is { } problem)
            throw new FormatException(string.Format(Strings.ArrayRule_Array, path, problem.Replace("\"min\"", Strings.ArrayRule_TheNumber).Replace("\"max\"", Strings.ArrayRule_TheLargestNumber)));
        return rule;

        int Whole(string text, string name)
            => int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new FormatException(string.Format(Strings.ArrayRule_ArrayTheMustBeAWhole, path, name));
    }
}
