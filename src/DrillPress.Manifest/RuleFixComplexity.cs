using System.Text.Json.Serialization;

namespace DrillPress;

/// <summary>Estimates the context and design judgment an agent needs to fix a typical violation, independently of severity or automatic fix availability.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RuleFixComplexity>))]
public enum RuleFixComplexity
{
    /// <summary>An isolated mechanical edit, typically on one line, requiring no surrounding code or design decisions.</summary>
    Trivial,

    /// <summary>A bounded change requiring nearby code or directly related symbols, with a clear correction and little design judgment.</summary>
    Local,

    /// <summary>A change requiring behavioral reasoning across related code and meaningful judgment, such as adding useful test coverage to a method.</summary>
    Complex,

    /// <summary>A change requiring broad context and substantial design judgment, such as correcting responsibilities, abstractions, or dependency boundaries across components.</summary>
    Architectural,
}
