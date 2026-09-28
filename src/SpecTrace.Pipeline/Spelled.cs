namespace SpecTrace.Pipeline;

public static class Spelled
{
    public static T Parse<T>(string spelled, Func<T, string> spell)
        where T : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(spelled);
        ArgumentNullException.ThrowIfNull(spell);

        foreach (var value in Enum.GetValues<T>())
        {
            if (spell(value) == spelled)
            {
                return value;
            }
        }

        throw new FormatException(
            $"'{spelled}' is not a {typeof(T).Name}; expected one of "
            + string.Join(", ", Enum.GetValues<T>().Select(spell)) + ".");
    }
}
