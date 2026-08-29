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

            var isDecimalPoint = character == '.'
                && index > 0
                && index < value.Length - 1
                && char.IsDigit(value[index - 1])
                && char.IsDigit(value[index + 1]);
            var isNumericSign = character is '-' or '+'
                && index < value.Length - 1
                && char.IsDigit(value[index + 1])
                && (result.Length == 0 || char.IsWhiteSpace(result[^1]));

            result.Append(isDecimalPoint || isNumericSign ? character : ' ');
        }

        return result.ToString();
    }
}
