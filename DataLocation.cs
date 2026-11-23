// Owner: Participant 3 – Data Developer
namespace Shkolo.Infrastructure.Persistence;

/// <summary>Определя в коя папка е JSON базата.</summary>
public static class DataLocation
{
    /// <summary>
    /// 1. <c>--data &lt;folder&gt;</c> when given.
    /// 2. Started from the source code (dotnet run / Visual Studio): the <c>JSON/Data</c> folder of the project,
    ///    src/Shkolo/JSON/Data, so school-data.json is visible in Solution Explorer next to the JSON code.
    /// 3. Otherwise (a published build): a <c>Data</c> folder next to the executable.
    /// </summary>
    public static string Resolve(string[] args, string baseDirectory)
    {
        var index = Array.IndexOf(args, "--data");
        if (index >= 0 && index + 1 < args.Length)
        {
            return Path.GetFullPath(args[index + 1]);
        }

        for (var dir = new DirectoryInfo(baseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Shkolo.csproj")))
            {
                return Path.Combine(dir.FullName, "JSON", "Data");
            }
        }

        return Path.Combine(baseDirectory, "Data");
    }
}
