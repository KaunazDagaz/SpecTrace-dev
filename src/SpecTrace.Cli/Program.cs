namespace SpecTrace.Cli;

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitUsage = 64;

    private static int Main(string[] args)
    {
        var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        Console.WriteLine($"spectrace {version}");
        Console.WriteLine();

        if (args.Length > 0)
        {
            Console.Error.WriteLine(
                $"'{args[0]}' is not implemented yet. This build scaffolds the solution only.");
            Console.Error.WriteLine();
            WriteUsage(Console.Error);
            return ExitUsage;
        }

        WriteUsage(Console.Out);
        return ExitSuccess;
    }

    private static void WriteUsage(TextWriter output)
    {
        output.WriteLine("Usage: spectrace <command> [options]");
        output.WriteLine();
        output.WriteLine("Commands — none implemented yet, each lands with its own task:");
        output.WriteLine("  run     --document <path>         extract, verify, generate, assemble the matrix");
        output.WriteLine("  score   --run <id> --gold <path>  score a run against a gold standard");
        output.WriteLine("  export  --run <id>                export the matrix");
        output.WriteLine();
        output.WriteLine("Concept, requirements and plan: https://github.com/KaunazDagaz/SpecTrace-docs");
    }
}
