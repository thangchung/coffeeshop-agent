namespace CounterService.Instructions;

public static class InstructionExtensions
{
    internal static string GetInstruction(string instructName)
    {
        string solutionDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        string instructFolder = Path.Combine(solutionDir, "Instructions");

        if (!Directory.Exists(instructFolder))
            throw new DirectoryNotFoundException("Instructions folder not found.");

        return File.ReadAllText(Path.Combine(instructFolder, $"{instructName}.md"));
    }
}
