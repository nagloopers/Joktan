using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Core.ViewModels;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt.ViewModels;

/// <summary>
/// One varying field in the message settings editor.
/// </summary>
public partial class FieldRuleViewModel : ViewModelBase
{
    public static IReadOnlyList<string> TypeNames { get; } =
    [
        Strings.FieldRule_UnixTimeSeconds, Strings.FieldRule_UnixTimeMilliseconds, Strings.FieldRule_ISOTimestamp, "GUID",
        Strings.FieldRule_Counter, Strings.FieldRule_RandomInteger, Strings.FieldRule_RandomNumber, Strings.FieldRule_OneOfAtRandom, Strings.FieldRule_EachInTurn, Strings.FieldRule_FixedValue,
    ];

    /// <summary>The kinds of fixed value, in the order of <see cref="ValueKind"/>.</summary>
    public static IReadOnlyList<string> ValueKindNames { get; } =
    [
        Strings.FieldRule_Text, Strings.FieldRule_Number, Strings.FieldRule_TrueOrFalse, Strings.FieldRule_Null, "JSON", Strings.FieldRule_FromAList,
    ];

    public enum ValueKind
    {
        Text,
        Number,
        Boolean,
        Null,
        Json,
        List,
    }

    public FieldRuleViewModel()
    {
    }

    public FieldRuleViewModel(FieldRule rule)
    {
        Path = rule.Path;
        TypeIndex = (int)rule.Type;
        Start = rule.Start.ToString(CultureInfo.InvariantCulture);
        Step = rule.Step.ToString(CultureInfo.InvariantCulture);
        if (rule.Type is FieldType.RandomInteger or FieldType.RandomNumber)
        {
            Min = rule.Min.ToString(CultureInfo.InvariantCulture);
            Max = rule.Max.ToString(CultureInfo.InvariantCulture);
        }

        Decimals = rule.Decimals.ToString(CultureInfo.InvariantCulture);
        Values = string.Join(", ", rule.Values.Select(v => v?.ToJsonString() ?? "null"));
        if (rule.Type == FieldType.Fixed)
        {
            if (rule.Values.Count > 0)
            {
                ValueKindIndex = (int)ValueKind.List;
                Choice = Display(rule.Value);
            }
            else
            {
                SetFixed(rule.Value);
            }
        }
    }

    [ObservableProperty]
    public partial string Path { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCounter), nameof(ShowRange), nameof(ShowDecimals), nameof(ShowValues),
        nameof(ShowFixed), nameof(ShowFixedText), nameof(ShowFixedSwitch), nameof(ShowChoices), nameof(ValuesLabel))]
    public partial int TypeIndex { get; set; }

    [ObservableProperty]
    public partial string Start { get; set; } = "1";

    [ObservableProperty]
    public partial string Step { get; set; } = "1";

    [ObservableProperty]
    public partial string Min { get; set; } = "0";

    [ObservableProperty]
    public partial string Max { get; set; } = "100";

    [ObservableProperty]
    public partial string Decimals { get; set; } = "2";

    [ObservableProperty]
    public partial string Values { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowValues), nameof(ShowFixedText), nameof(ShowFixedSwitch), nameof(ShowChoices),
        nameof(ValuesLabel), nameof(FixedPlaceholder))]
    public partial int ValueKindIndex { get; set; }

    /// <summary>A fixed value written as text, a number or JSON, depending on the kind.</summary>
    [ObservableProperty]
    public partial string FixedText { get; set; } = "";

    [ObservableProperty]
    public partial bool FixedBool { get; set; }

    /// <summary>The values listed in <see cref="Values"/>, as offered in the dropdown.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Choices { get; set; } = [];

    /// <summary>The value picked from <see cref="Choices"/>.</summary>
    [ObservableProperty]
    public partial string? Choice { get; set; }

    public FieldType Type => (FieldType)Math.Max(TypeIndex, 0);

    public ValueKind Kind => (ValueKind)Math.Max(ValueKindIndex, 0);

    public bool ShowCounter => Type == FieldType.Counter;

    public bool ShowRange => Type is FieldType.RandomInteger or FieldType.RandomNumber;

    public bool ShowDecimals => Type == FieldType.RandomNumber;

    public bool ShowValues => Type is FieldType.OneOf or FieldType.Cycle || ShowChoices;

    public bool ShowFixed => Type == FieldType.Fixed;

    public bool ShowFixedText => ShowFixed && Kind is ValueKind.Text or ValueKind.Number or ValueKind.Json;

    public bool ShowFixedSwitch => ShowFixed && Kind == ValueKind.Boolean;

    public bool ShowChoices => ShowFixed && Kind == ValueKind.List;

    public string ValuesLabel => ShowFixed ? Strings.FieldRule_AllowedValuesSeparatedByCommas : Strings.FieldRule_ValuesSeparatedByCommas;

    public string FixedPlaceholder => Kind switch
    {
        ValueKind.Number => "42",
        ValueKind.Json => "{\"id\": 1}",
        _ => "",
    };

    /// <summary>Shows a value in the editor: its kind and what it is set to.</summary>
    public void SetFixed(JsonNode? value)
    {
        var kind = value?.GetValueKind() switch
        {
            null or JsonValueKind.Null => ValueKind.Null,
            JsonValueKind.String => ValueKind.Text,
            JsonValueKind.Number => ValueKind.Number,
            JsonValueKind.True or JsonValueKind.False => ValueKind.Boolean,
            _ => ValueKind.Json,
        };

        FixedText = kind == ValueKind.Text ? value!.GetValue<string>() : value?.ToJsonString() ?? "";
        FixedBool = kind == ValueKind.Boolean && value!.GetValue<bool>();
        ValueKindIndex = (int)kind;
    }

    partial void OnValuesChanged(string value) => RefreshChoices();

    partial void OnValueKindIndexChanged(int value)
    {
        // Start a list with the value written so far.
        if (Kind == ValueKind.List && Values.Trim().Length == 0 && FixedText.Trim().Length > 0)
            Values = FixedText;
        else if (Kind == ValueKind.List && Choice is null)
            RefreshChoices();
    }

    // Rebuilds the dropdown, keeping the picked value while it is still listed.
    private void RefreshChoices()
    {
        var keep = Choice;
        var choices = ParseValues().Select(Display).Distinct().ToList();
        Choices = choices;
        Choice = keep is not null && choices.Contains(keep) ? keep : choices.FirstOrDefault();
    }

    // A string is shown without its quotes; anything else as its JSON text.
    private static string Display(JsonNode? value)
        => value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value?.ToJsonString() ?? "null";

    /// <exception cref="FormatException">A setting is missing or not a number.</exception>
    public FieldRule ToRule()
    {
        var path = Path.Trim();
        if (path.Length == 0)
            throw new FormatException(Strings.FieldRule_EveryVaryingFieldNeedsAField);

        var rule = new FieldRule(path, Type);
        rule = Type switch
        {
            FieldType.Counter => rule with
            {
                Start = Whole(Start, Strings.FieldRule_Start),
                Step = Whole(Step, Strings.FieldRule_Step),
            },
            FieldType.RandomInteger => rule with
            {
                Min = Whole(Min, Strings.FieldRule_Minimum),
                Max = Whole(Max, Strings.FieldRule_Maximum),
            },
            FieldType.RandomNumber => rule with
            {
                Min = Number(Min, "minimum"),
                Max = Number(Max, "maximum"),
                Decimals = (int)Whole(Decimals, Strings.FieldRule_Decimals),
            },
            FieldType.OneOf or FieldType.Cycle => rule with { Values = ParseValues() },
            FieldType.Fixed => FixedRule(rule),
            _ => rule,
        };

        if (MessageSettings.CheckRule(rule) is { } problem)
            throw new FormatException(string.Format(Strings.FieldRule_Field, path, problem.Replace("\"", "")));
        return rule;

        FieldRule FixedRule(FieldRule start)
        {
            switch (Kind)
            {
                case ValueKind.Text:
                    return start with { Value = JsonValue.Create(FixedText) };

                case ValueKind.Number:
                    var text = FixedText.Trim();
                    return start with
                    {
                        Value = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)
                            ? JsonValue.Create(whole)
                            : JsonValue.Create(Number(text, "value")),
                    };

                case ValueKind.Boolean:
                    return start with { Value = JsonValue.Create(FixedBool) };

                case ValueKind.Json:
                    try
                    {
                        return start with { Value = JsonNode.Parse(FixedText, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false }) };
                    }
                    catch (JsonException)
                    {
                        throw new FormatException(string.Format(Strings.FieldRule_FieldTheValueIsNotValid, path));
                    }

                case ValueKind.List:
                    var values = ParseValues();
                    if (values.Length == 0)
                        throw new FormatException(string.Format(Strings.FieldRule_FieldListAtLeastOneAllowed, path));
                    var picked = values.FirstOrDefault(v => Display(v) == Choice, values[0]);
                    return start with { Values = values, Value = picked?.DeepClone() };

                default:
                    return start with { Value = null };
            }
        }

        long Whole(string text, string name)
            => long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new FormatException(string.Format(Strings.FieldRule_FieldTheMustBeAWhole, path, name));

        double Number(string text, string name)
            => double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new FormatException(string.Format(Strings.FieldRule_FieldTheMustBeANumber, path, name));
    }

    // Each comma-separated item is read as JSON when it is JSON (42, true, "text"), otherwise as text.
    private JsonNode?[] ParseValues()
    {
        return Values.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item =>
            {
                try
                {
                    return JsonNode.Parse(item);
                }
                catch (JsonException)
                {
                    return JsonValue.Create(item);
                }
            })
            .ToArray();
    }
}
