namespace SIL.Motif.App.ViewModels;

public sealed record EnvironmentToken(string Raw, string Explanation, bool IsProjectDefined);

/// <summary>Decodes known punctuation in authored environment text; callers must establish its source.</summary>
public static class EnvironmentNotation
{
    public static IReadOnlyList<EnvironmentToken> Decode(string notation)
    {
        var tokens = new List<EnvironmentToken>();
        for (var index = 0; index < notation.Length;)
        {
            var start = index;
            var character = notation[index++];
            var explanation = character switch
            {
                '/' => "Environment begins",
                '_' => "Target position",
                '#' => "Word edge",
                '(' => "Optional group begins",
                ')' => "Optional group ends",
                _ => null,
            };
            if (character == '[')
            {
                var end = notation.IndexOf(']', index);
                if (end >= 0)
                {
                    var name = notation[index..end];
                    index = end + 1;
                    if (name.Length > 0 && name.All(value => char.IsLetterOrDigit(value) || value is '-' or ' '))
                        explanation = $"Natural class {name}; membership is project-defined";
                }
                else index = notation.Length;
            }
            else if (explanation is null)
                while (index < notation.Length && notation[index] is not ('/' or '_' or '#' or '(' or ')' or '[')) index++;
            tokens.Add(new EnvironmentToken(notation[start..index],
                explanation ?? "Project-defined notation; interpretation unavailable", explanation is null));
        }
        return tokens;
    }
}
