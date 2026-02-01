namespace WorkflowEngine.Tests.UI.Backend.Data;

public static class LexoRankGenerator
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

    public static string First() => "a0";

    public static string Next(string current)
    {
        var chars = current.ToCharArray();
        var lastChar = chars[^1];
        var index = Alphabet.IndexOf(lastChar);

        if (index < Alphabet.Length - 1)
        {
            chars[^1] = Alphabet[index + 1];
            return new string(chars);
        }

        // If we reached the end of alphabet, add new digit
        return current + "0";
    }

    public static string Between(string before, string after)
    {
        if (string.IsNullOrEmpty(before))
            return First();

        if (string.IsNullOrEmpty(after))
            return Next(before);

        // Simple mid-point calculation
        if (before.Length < after.Length)
        {
            return before + "m";
        }

        var beforeChars = before.ToCharArray();
        var afterChars = after.ToCharArray();
        var minLength = Math.Min(beforeChars.Length, afterChars.Length);

        for (int i = 0; i < minLength; i++)
        {
            var beforeIndex = Alphabet.IndexOf(beforeChars[i]);
            var afterIndex = Alphabet.IndexOf(afterChars[i]);

            if (afterIndex - beforeIndex > 1)
            {
                var midIndex = (beforeIndex + afterIndex) / 2;
                var result = before.Substring(0, i) + Alphabet[midIndex];
                return result;
            }

            if (beforeIndex != afterIndex)
            {
                return before + "m";
            }
        }

        return before + "m";
    }
}
