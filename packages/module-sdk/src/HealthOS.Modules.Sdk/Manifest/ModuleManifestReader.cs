using System.Reflection;
using System.Text.Json;

namespace HealthOS.Modules.Sdk.Manifest;

public static class ModuleManifestReader
{
    public const string EmbeddedResourceName = "module.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static ModuleManifest Parse(string json)
    {
        // "$schema" is allowed in the file for editor support but is not part of the model.
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var withoutSchema = new Dictionary<string, JsonElement>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Name != "$schema")
            {
                withoutSchema[p.Name] = p.Value;
            }
        }

        var normalized = JsonSerializer.Serialize(withoutSchema);
        return JsonSerializer.Deserialize<ModuleManifest>(normalized, Options)
            ?? throw new InvalidDataException("Manifest is empty.");
    }

    /// <summary>Reads the manifest embedded in a module assembly (build-time packaged module).</summary>
    public static ModuleManifest FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"Assembly '{assembly.GetName().Name}' has no embedded '{EmbeddedResourceName}'. " +
                "Add <EmbeddedResource Include=\"..\\..\\module.json\" LogicalName=\"module.json\" /> to the module project.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
