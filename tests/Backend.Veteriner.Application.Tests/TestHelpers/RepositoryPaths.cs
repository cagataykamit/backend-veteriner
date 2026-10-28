namespace Backend.Veteriner.Application.Tests.TestHelpers;

/// <summary>Kaynak dosyaları tarayan mimari testleri için depo yolları.</summary>
internal static class RepositoryPaths
{
    public static string Src => Path.Combine(Root(), "src");

    /// <summary><c>src</c> altındaki C# dosyaları (bin/obj hariç).</summary>
    public static IEnumerable<string> SourceFiles()
        => Directory.EnumerateFiles(Src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    /// <summary><c>src</c>'ye göre, <c>/</c> ayraçlı yol.</summary>
    public static string RelativeToSrc(string path)
        => Path.GetRelativePath(Src, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Veteriner.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Veteriner.sln bulunamadı.");
    }
}
