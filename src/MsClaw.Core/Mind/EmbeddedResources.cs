namespace MsClaw.Core;

internal static class EmbeddedResources
{
    private const string ResourcePrefix = "MsClaw.Core.Mind.Templates.";

    /// <summary>Reads a template file from the embedded resources by simple filename (root-level templates only).</summary>
    public static string ReadTemplate(string fileName)
    {
        var resourceName = $"{ResourcePrefix}{fileName}";
        return ReadResource(resourceName);
    }

    /// <summary>Reads a template file by its exact embedded resource name suffix (for nested paths).</summary>
    public static string ReadTemplateByResourceName(string resourceSuffix)
    {
        var resourceName = $"{ResourcePrefix}{resourceSuffix}";
        return ReadResource(resourceName);
    }

    private static string ReadResource(string resourceName)
    {
        var assembly = typeof(EmbeddedResources).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
