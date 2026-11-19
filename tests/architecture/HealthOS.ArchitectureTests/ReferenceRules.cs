using System.Xml.Linq;
using HealthOS.Modules.Sdk.Manifest;

namespace HealthOS.ArchitectureTests;

/// <summary>
/// Static checks over project files and module manifests. Works on any directory laid out like the
/// repository (modules/, packages/, services/), so it can be proven against fixture repositories.
/// </summary>
public static class ReferenceRules
{
    public enum ProjectRole
    {
        Sdk,
        ModuleContracts,
        ModuleInternal,
        Host,
        Other,
    }

    public sealed record ProjectInfo(string Path, string Name, ProjectRole Role, string? ModuleId, IReadOnlyList<string> References);

    public static IReadOnlyList<string> Check(string root)
    {
        var manifests = LoadManifests(root);
        var projects = LoadProjects(root, manifests);
        var byPath = projects.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
        var violations = new List<string>();

        foreach (var p in projects)
        {
            foreach (var refPath in p.References)
            {
                if (!byPath.TryGetValue(refPath, out var target))
                {
                    continue; // test projects / unknown: not governed here
                }

                var why = Violation(p, target, manifests);
                if (why is not null)
                {
                    violations.Add($"{p.Name} -> {target.Name}: {why}");
                }
            }
        }

        return violations;
    }

    private static string? Violation(ProjectInfo from, ProjectInfo to, Dictionary<string, ModuleManifest> manifests)
    {
        // Only hosts (composition roots) may reference module internals; a module may reference its own contracts only.
        if (to.Role == ProjectRole.ModuleInternal && from.Role != ProjectRole.Host)
        {
            return "only host composition roots may reference a module's internal project";
        }

        switch (from.Role)
        {
            case ProjectRole.Sdk:
                return to.Role is ProjectRole.ModuleContracts or ProjectRole.ModuleInternal or ProjectRole.Host
                    ? "the SDK must not depend on modules or hosts"
                    : null;

            case ProjectRole.ModuleInternal:
            case ProjectRole.ModuleContracts:
                if (to.Role == ProjectRole.Sdk)
                {
                    return from.Role == ProjectRole.ModuleContracts ? "contracts must stay dependency-free (no SDK reference)" : null;
                }

                if (to.Role == ProjectRole.ModuleContracts)
                {
                    if (to.ModuleId == from.ModuleId)
                    {
                        return from.Role == ProjectRole.ModuleInternal ? null : "contracts project cannot reference itself";
                    }

                    var declared = manifests[from.ModuleId!].Dependencies.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
                    return declared.Contains(to.ModuleId!)
                        ? null
                        : $"'{to.ModuleId}' is not a declared dependency of '{from.ModuleId}' in module.json";
                }

                return to.Role == ProjectRole.Host ? "modules must not reference hosts" : null;

            default:
                return null;
        }
    }

    public static Dictionary<string, ModuleManifest> LoadManifests(string root)
    {
        var result = new Dictionary<string, ModuleManifest>(StringComparer.Ordinal);
        var modulesDir = System.IO.Path.Combine(root, "modules");
        if (!Directory.Exists(modulesDir))
        {
            return result;
        }

        foreach (var file in Directory.GetFiles(modulesDir, "module.json", SearchOption.AllDirectories))
        {
            var m = ModuleManifestReader.Parse(File.ReadAllText(file));
            result[m.Id] = m;
        }

        return result;
    }

    private static List<ProjectInfo> LoadProjects(string root, Dictionary<string, ModuleManifest> manifests)
    {
        var moduleDirToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(System.IO.Path.Combine(root, "modules"), "module.json", SearchOption.AllDirectories))
        {
            moduleDirToId[System.IO.Path.GetDirectoryName(file)!] = ModuleManifestReader.Parse(File.ReadAllText(file)).Id;
        }

        var projects = new List<ProjectInfo>();
        foreach (var top in new[] { "modules", "packages", "services" })
        {
            var dir = System.IO.Path.Combine(root, top);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var csproj in Directory.GetFiles(dir, "*.csproj", SearchOption.AllDirectories))
            {
                var full = System.IO.Path.GetFullPath(csproj);
                var segments = System.IO.Path.GetRelativePath(root, full)
                    .Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                if (segments.Contains("tests", StringComparer.OrdinalIgnoreCase) ||
                    segments.Contains("bin", StringComparer.OrdinalIgnoreCase) ||
                    segments.Contains("obj", StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = System.IO.Path.GetFileNameWithoutExtension(full);
                string? moduleId = moduleDirToId.FirstOrDefault(kv => full.StartsWith(kv.Key + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).Value;
                var role = top switch
                {
                    "modules" when moduleId is not null => name.EndsWith(".Contracts", StringComparison.Ordinal) ? ProjectRole.ModuleContracts : ProjectRole.ModuleInternal,
                    "packages" when name == "HealthOS.Modules.Sdk" => ProjectRole.Sdk,
                    "services" => ProjectRole.Host,
                    _ => ProjectRole.Other,
                };

                var refs = XDocument.Load(full)
                    .Descendants("ProjectReference")
                    .Select(e => e.Attribute("Include")?.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(full)!, v!.Replace('\\', System.IO.Path.DirectorySeparatorChar))))
                    .ToList();

                projects.Add(new ProjectInfo(full, name, role, moduleId, refs));
            }
        }

        _ = manifests;
        return projects;
    }
}
