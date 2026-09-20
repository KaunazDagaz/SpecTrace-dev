namespace SpecTrace.Core;

public readonly record struct TextSpan(int Start, int End)
{
    public int Length => End - Start;

    public bool Overlaps(TextSpan other) => Start < other.End && other.Start < End;
}
