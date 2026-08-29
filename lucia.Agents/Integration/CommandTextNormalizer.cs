using System.Text;

namespace lucia.Agents.Integration;

public static class CommandTextNormalizer
{
    public static string NormalizePunctuation(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsLetterOrDigit(character)
                || char.IsWhiteSpace(character)
                || character == '_')
            {
                result.Append(character);
                continue;
            }

            var isNumericBoundary = result.Length == 0
                || char.IsWhiteSpace(result[^1])
                || result[^1] is '-' or '+';
            var isDecimalPoint = character == '.'
                && index < value.Length - 1
                && char.IsDigit(value[index + 1])
                && ((index > 0 && char.IsDigit(value[index - 1])) || isNumericBoundary);
            var isNumericSign = character is '-' or '+'
                && index < value.Length - 1
                && (char.IsDigit(value[index + 1])
                    || (value[index + 1] == '.'
                        && index < value.Length - 2
                        && char.IsDigit(value[index + 2])))
                && isNumericBoundary;

            result.Append(isDecimalPoint || isNumericSign ? character : ' ');
        }

        return result.ToString();
    }
}
