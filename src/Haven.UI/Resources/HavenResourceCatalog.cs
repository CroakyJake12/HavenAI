namespace Haven.UI;

/// <summary>Loads the four central, human-editable Haven class/animation resources.</summary>
public static class HavenResourceCatalog
{
    public static string SystemClasses => Read("SystemClasses.hui");
    public static string UserClasses => Read("UserClasses.hui");
    public static string SystemAnimations => Read("SystemAnimations.hui");
    public static string UserAnimations => Read("UserAnimations.hui");

    private static string Read(string fileName)
    {
        var assembly = typeof(HavenResourceCatalog).Assembly;
        var name = assembly.GetManifestResourceNames()
            .SingleOrDefault(candidate => candidate.EndsWith(fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded Haven.UI resource '{fileName}' is missing.");
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded Haven.UI resource '{fileName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
