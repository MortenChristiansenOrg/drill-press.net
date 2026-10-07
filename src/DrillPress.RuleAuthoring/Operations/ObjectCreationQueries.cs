namespace DrillPress;

/// <summary>Filters for object creations.</summary>
public static class ObjectCreationQueries
{
    /// <summary>Selects creations of exactly this type; open generic descriptors match every construction.</summary>
    public static CodeQuery<CodeObjectCreation> Of(
        this CodeQuery<CodeObjectCreation> creations,
        CodeType type
    ) => creations.Where(creation => creation.Creates(type));

    /// <summary>Selects creations of exactly this type, including constructed generic arguments.</summary>
    public static CodeQuery<CodeObjectCreation> Of<T>(
        this CodeQuery<CodeObjectCreation> creations
    ) => creations.Of(CodeType.Of<T>());
}
