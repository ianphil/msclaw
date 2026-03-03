namespace MsClaw.Core;

internal static class EmbeddedResources
{
    public static string ReadTemplate(string fileName)
    {
        var assembly = typeof(EmbeddedResources).Assembly;
        var resourceName = $"MsClaw.Core.Mind.Templates.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
