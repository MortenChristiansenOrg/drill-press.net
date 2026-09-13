using System.Globalization;
using System.Text;

namespace DrillPress.Semantics;

internal static class GenericTypeName
{
    internal static string Normalize(string metadataName)
    {
        if (metadataName.AsSpan().IndexOfAny('<', '>') < 0)
        {
            return metadataName;
        }

        var result = new StringBuilder(metadataName.Length);
        var segmentStart = 0;
        var insideType = false;
        for (var index = 0; index < metadataName.Length; index++)
        {
            var character = metadataName[index];
            if (character == '<')
            {
                var arity = ReadArity(metadataName, segmentStart, ref index);
                result.Append('`').Append(arity.ToString(CultureInfo.InvariantCulture));
                insideType = true;
            }
            else if (character == '>')
            {
                throw InvalidName(metadataName);
            }
            else
            {
                if (character == '.' && insideType)
                {
                    if (
                        index == segmentStart
                        || metadataName[index - 1] == ']'
                        || index + 1 == metadataName.Length
                        || !(
                            char.IsLetter(metadataName[index + 1]) || metadataName[index + 1] == '_'
                        )
                    )
                    {
                        throw InvalidName(metadataName);
                    }

                    result.Append('+');
                }
                else
                {
                    result.Append(character);
                }

                if (character == '+')
                {
                    insideType = true;
                }

                if (character is '.' or '+')
                {
                    segmentStart = index + 1;
                }
            }
        }

        return result.ToString();
    }

    private static int ReadArity(string metadataName, int segmentStart, ref int index)
    {
        if (
            index == segmentStart
            || !(char.IsLetterOrDigit(metadataName[index - 1]) || metadataName[index - 1] == '_')
            || metadataName.AsSpan(segmentStart, index - segmentStart).Contains('`')
        )
        {
            throw InvalidName(metadataName);
        }

        var arity = 1;
        while (++index < metadataName.Length)
        {
            var character = metadataName[index];
            if (character == '>')
            {
                if (
                    index + 1 < metadataName.Length
                    && metadataName[index + 1] is not ('.' or '+' or '[')
                )
                {
                    throw InvalidName(metadataName);
                }

                return arity;
            }

            if (character == ',')
            {
                arity++;
            }
            else if (!char.IsWhiteSpace(character))
            {
                throw InvalidName(metadataName);
            }
        }

        throw InvalidName(metadataName);
    }

    private static ArgumentException InvalidName(string metadataName) =>
        new(
            $"Invalid open generic type name '{metadataName}'. Use empty slots such as <> or <,>, Outer<>.Inner<,> for nested types (+ also works), or CodeType.Of<T>() for constructed types.",
            nameof(metadataName)
        );
}
