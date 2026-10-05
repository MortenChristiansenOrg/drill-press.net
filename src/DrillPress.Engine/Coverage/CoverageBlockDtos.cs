using System.Reflection.Metadata;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress.Engine;

internal sealed record CoverageInstruction(
    int Offset,
    int End,
    ILOpCode OpCode,
    int Operand,
    int[] Targets
);

internal sealed record CoverageSymbolEvidence(
    string Identity,
    CoverageCallProof[] Calls,
    CodeInvocation[] Occurrences
);

internal sealed record CoverageCallCompilation(
    CoverageCallProof[] Proofs,
    CodeInvocation[] Occurrences
);

internal sealed record CoveragePointLayout(string DocumentId, TextSpan Span, int[] Ordinals);

internal sealed record CoverageCallProof(
    string DocumentId,
    TextSpan Occurrence,
    TextSpan Point,
    int Token,
    CoveragePointLayout[] Layout,
    int Entry,
    bool SafePrefix,
    int? Completion,
    int InstructionOffset
);
