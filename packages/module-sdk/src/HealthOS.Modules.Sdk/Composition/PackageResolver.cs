using HealthOS.Modules.Sdk.Manifest;
using Semver;

namespace HealthOS.Modules.Sdk.Composition;

public sealed record PackageResolution(IReadOnlyList<ModuleManifest> OrderedModules, IReadOnlyList<ManifestIssue> Issues)
{
    public bool Succeeded => Issues.Count == 0;
}

/// <summary>
/// Package-level resolution: validates the set of modules compiled into a deployment package
/// (manifests, core compatibility, dependencies, kind rules, schema ownership, cycles) and returns
/// them in dependency order. Tenant-level activation (entitlements, admin enablement,
/// incompatibilities per site) is a separate step introduced in M3.
/// </summary>
public static class PackageResolver
{
    public static PackageResolution Resolve(IEnumerable<ModuleManifest> manifests, string coreVersion)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        var list = manifests.ToList();
        var issues = new List<ManifestIssue>();
        var core = SemVersion.Parse(coreVersion, SemVersionStyles.Strict);

        foreach (var m in list)
        {
            issues.AddRange(ManifestValidator.Validate(m));
        }

        var byId = new Dictionary<string, ModuleManifest>(StringComparer.Ordinal);
        foreach (var m in list)
        {
            if (!byId.TryAdd(m.Id, m))
            {
                issues.Add(new(m.Id, "DUPLICATE_MODULE", "Module is included more than once in the package."));
            }
        }

        var schemaOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in byId.Values)
        {
            if (m.Database is { } db && !schemaOwners.TryAdd(db.Schema, m.Id))
            {
                issues.Add(new(m.Id, "SCHEMA_CONFLICT", $"Schema '{db.Schema}' is already owned by '{schemaOwners[db.Schema]}'."));
            }

            if (SemVersionRange.TryParseNpm(m.RequiresCore, out var coreRange) && !coreRange.Contains(core))
            {
                issues.Add(new(m.Id, "CORE_INCOMPATIBLE", $"Requires core {m.RequiresCore}; package core is {coreVersion}."));
            }

            CheckDependencies(m, byId, issues);
            CheckKindRules(m, byId, issues);
        }

        var ordered = TopologicalOrder(byId, issues);
        return new PackageResolution(issues.Count == 0 ? ordered : [], issues);
    }

    private static void CheckDependencies(ModuleManifest m, Dictionary<string, ModuleManifest> byId, List<ManifestIssue> issues)
    {
        foreach (var dep in m.Dependencies)
        {
            if (!byId.TryGetValue(dep.Id, out var target))
            {
                issues.Add(new(m.Id, "MISSING_DEPENDENCY", $"Requires '{dep.Id}' ({dep.Version}), which is not in the package."));
                continue;
            }

            if (SemVersionRange.TryParseNpm(dep.Version, out var range) &&
                SemVersion.TryParse(target.Version, SemVersionStyles.Strict, out var actual) &&
                !range.Contains(actual))
            {
                issues.Add(new(m.Id, "VERSION_INCOMPATIBLE", $"Requires '{dep.Id}' {dep.Version}; package has {target.Version}."));
            }
        }
    }

    private static void CheckKindRules(ModuleManifest m, Dictionary<string, ModuleManifest> byId, List<ManifestIssue> issues)
    {
        var depKinds = m.Dependencies
            .Where(d => byId.ContainsKey(d.Id))
            .Select(d => byId[d.Id])
            .ToList();

        switch (m.Kind)
        {
            case ModuleKind.Platform:
                foreach (var d in depKinds.Where(d => d.Kind != ModuleKind.Platform))
                {
                    issues.Add(new(m.Id, "KIND_RULE", $"Platform modules may only depend on platform modules (found '{d.Id}', {d.Kind})."));
                }

                break;
            case ModuleKind.Domain:
                foreach (var d in depKinds.Where(d => d.Kind is not (ModuleKind.Platform or ModuleKind.Domain)))
                {
                    issues.Add(new(m.Id, "KIND_RULE", $"Domain modules may only depend on platform/domain modules (found '{d.Id}', {d.Kind})."));
                }

                break;
            case ModuleKind.Product:
                foreach (var d in depKinds.Where(d => d.Kind is ModuleKind.Product or ModuleKind.Pack or ModuleKind.Bridge))
                {
                    // Exception: a product may depend on another module of the same product family (e.g. clinicos.billing -> clinicos.core).
                    if (d.Kind == ModuleKind.Product && SameFamily(m.Id, d.Id))
                    {
                        continue;
                    }

                    issues.Add(new(m.Id, "KIND_RULE", $"Products must not depend on other products; use a bridge module (found '{d.Id}')."));
                }

                break;
            case ModuleKind.Pack:
                if (depKinds.Count(d => d.Kind == ModuleKind.Product) != 1)
                {
                    issues.Add(new(m.Id, "KIND_RULE", "A pack must depend on exactly one parent product module."));
                }

                break;
            case ModuleKind.Bridge:
                if (depKinds.Where(d => d.Kind == ModuleKind.Product).Select(d => Family(d.Id)).Distinct().Count() < 2)
                {
                    issues.Add(new(m.Id, "KIND_RULE", "A bridge must depend on products from at least two product families."));
                }

                break;
            case ModuleKind.Ai:
                break;
        }
    }

    private static string Family(string id) => id.Split('.')[0];

    private static bool SameFamily(string a, string b) => Family(a) == Family(b);

    private static List<ModuleManifest> TopologicalOrder(Dictionary<string, ModuleManifest> byId, List<ManifestIssue> issues)
    {
        var result = new List<ModuleManifest>();
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 0 = unvisited, 1 = visiting, 2 = done

        bool Visit(string id, Stack<string> path)
        {
            if (!byId.TryGetValue(id, out var m))
            {
                return true; // missing dependency already reported
            }

            switch (state.GetValueOrDefault(id))
            {
                case 2:
                    return true;
                case 1:
                    issues.Add(new(id, "CYCLE", "Dependency cycle: " + string.Join(" -> ", path.Reverse().Append(id))));
                    return false;
            }

            state[id] = 1;
            path.Push(id);
            foreach (var dep in m.Dependencies)
            {
                if (!Visit(dep.Id, path))
                {
                    path.Pop();
                    return false;
                }
            }

            path.Pop();
            state[id] = 2;
            result.Add(m);
            return true;
        }

        foreach (var id in byId.Keys.Order(StringComparer.Ordinal))
        {
            if (!Visit(id, new Stack<string>()))
            {
                break;
            }
        }

        return result;
    }
}
