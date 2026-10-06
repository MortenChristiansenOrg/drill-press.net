namespace DrillPress.Presets;

/// <summary>A declared operation's role, independent of whether a particular call enumerates an input.</summary>
public enum LinqOperationCategory
{
    /// <summary>Produces a scalar or selected element, including collection fast paths.</summary>
    Scalar,

    /// <summary>Produces an eager collection from its input.</summary>
    Materializer,

    /// <summary>Changes the sequence view without materializing it.</summary>
    Adapter,

    /// <summary>Constructs a deferred sequence or provider-facing query.</summary>
    DeferredConstruction,

    /// <summary>Produces a sequence without a sequence input; includes Empty, Range and Repeat.</summary>
    SequenceFactory,
}

/// <summary>The framework declaring surface; Queryable does not identify a provider.</summary>
public enum LinqSurface
{
    /// <summary>System.Linq.Enumerable, including its collection fast paths.</summary>
    Enumerable,

    /// <summary>System.Linq.Queryable's provider-facing operations.</summary>
    Queryable,
}

/// <summary>Reviewed declaration behavior for direct sequence inputs during the invocation, independent of execution evidence or later consumption of a returned sequence.</summary>
public enum LinqSequenceConsumption
{
    /// <summary>No maintained consumption claim; includes provider-facing operations and unreviewed construction behavior.</summary>
    Unknown,

    /// <summary>The framework operation does not acquire or advance an input enumerator. Count getters and other invoked user code may still have effects, including indirect enumeration.</summary>
    NeverEnumerates,

    /// <summary>The operation can acquire or advance an input enumerator. Fast paths and empty inputs remain possible; this does not assert consumption of any particular input or call.</summary>
    MayEnumerate,
}

/// <summary>Explains why a call has or lacks maintained classification data.</summary>
public enum LinqClassificationStatus
{
    /// <summary>The framework and exact normalized overload are in the maintained catalogue.</summary>
    Supported,

    /// <summary>The evaluated framework is outside the catalogue's supported versions.</summary>
    UnsupportedFramework,

    /// <summary>A real standard surface declares an overload absent from this framework's catalogue.</summary>
    UnsupportedOperation,

    /// <summary>The declaration is outside the signed Enumerable and Queryable framework surfaces.</summary>
    NotStandardSymbol,

    /// <summary>The source call does not bind successfully.</summary>
    Unresolved,
}

/// <summary>A written sequence input, before contextual conversion, in source evaluation order.</summary>
/// <param name="Role">The normalized declaration's sequence parameter name.</param>
/// <param name="Value">The original value; an extension receiver appears once in either call spelling.</param>
public sealed record LinqSequenceInput(string Role, CodeExpression Value);

/// <summary>Maintained declaration facts; classification never proves execution, enumeration, purity or provider identity.</summary>
/// <param name="Status">Whether this exact overload is supported in the evaluated framework.</param>
/// <param name="Surface">The standard declaration surface, when recognized.</param>
/// <param name="Category">The maintained role, available only for supported calls.</param>
/// <param name="SequenceInputs">Direct sequence inputs only; excludes callbacks, comparers and sequences returned by callbacks.</param>
public sealed record LinqOperation(
    LinqClassificationStatus Status,
    LinqSurface? Surface,
    LinqOperationCategory? Category,
    IReadOnlyList<LinqSequenceInput> SequenceInputs
)
{
    /// <summary>Maintained consumption behavior for this supported exact declaration. Unknown supplies no guarantee; this fact never proves execution, purity, provider behavior or later deferred consumption.</summary>
    public LinqSequenceConsumption SequenceConsumption { get; init; }
}

internal sealed record LinqCatalogueEntry(
    string Signature,
    LinqOperationCategory Category,
    int Frameworks,
    int[] SequenceParameters,
    LinqSequenceConsumption SequenceConsumption
);
