using HealthOS.Modules.Sdk.Manifest;

namespace HealthOS.Modules.Sdk.Composition;

public enum ActivationState
{
    /// <summary>Not usable in this tenant (APIs return 404, hidden from navigation). Data is retained.</summary>
    Inactive,

    /// <summary>Usable.</summary>
    Active,

    /// <summary>Licence expired: data readable, writes blocked.</summary>
    ReadOnly,
}

public enum LicenseState
{
    None,
    Active,
    ReadOnly,
}

public sealed record ActivationReason(string Code, string Message);

public sealed record ModuleActivation(ModuleManifest Manifest, bool Enabled, ActivationState State, IReadOnlyList<ActivationReason> Reasons)
{
    public string Id => Manifest.Id;
}

public sealed record EnablePlan(bool Allowed, IReadOnlyList<string> ModulesToEnable, IReadOnlyList<ActivationReason> Problems);

/// <summary>
/// Tenant-level activation. A module is active in a tenant when it is in the package, and
/// - platform modules: always;
/// - other modules: enabled by the tenant, licensed (if it declares a licence), all dependencies active,
///   and not incompatible with another enabled module.
/// Pure and deterministic so every rule is unit-testable; the data comes from core.modules / core.licensing.
/// </summary>
public static class TenantActivation
{
    public static IReadOnlyList<ModuleActivation> Resolve(
        IReadOnlyList<ModuleManifest> package,
        IReadOnlySet<string> enabled,
        Func<ModuleLicense, LicenseState> license)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(enabled);
        ArgumentNullException.ThrowIfNull(license);

        var byId = package.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var results = new Dictionary<string, ModuleActivation>(StringComparer.Ordinal);

        ModuleActivation Evaluate(string id, HashSet<string> visiting)
        {
            if (results.TryGetValue(id, out var done))
            {
                return done;
            }

            var m = byId[id];
            if (!visiting.Add(id))
            {
                return new ModuleActivation(m, enabled.Contains(id), ActivationState.Inactive, [new("CYCLE", "Dependency cycle.")]);
            }

            var reasons = new List<ActivationReason>();
            var isPlatform = m.Kind == ModuleKind.Platform;
            var isEnabled = isPlatform || enabled.Contains(id);
            var state = ActivationState.Active;

            if (!isEnabled)
            {
                reasons.Add(new("NOT_ENABLED", "Not enabled for this tenant."));
                state = ActivationState.Inactive;
            }

            if (!isPlatform && m.License is { } l)
            {
                switch (license(l))
                {
                    case LicenseState.None:
                        reasons.Add(new("NOT_ENTITLED", $"No licence for {l.ProductCode}:{l.Feature}."));
                        state = ActivationState.Inactive;
                        break;
                    case LicenseState.ReadOnly:
                        reasons.Add(new("LICENSE_EXPIRED", "Licence expired: read-only."));
                        if (state == ActivationState.Active)
                        {
                            state = ActivationState.ReadOnly;
                        }

                        break;
                }
            }

            foreach (var dep in m.Dependencies)
            {
                if (!byId.ContainsKey(dep.Id))
                {
                    reasons.Add(new("MISSING_DEPENDENCY", $"Requires '{dep.Id}', which is not installed in this deployment."));
                    state = ActivationState.Inactive;
                    continue;
                }

                var d = Evaluate(dep.Id, visiting);
                if (d.State == ActivationState.Inactive)
                {
                    reasons.Add(new("DEPENDENCY_INACTIVE", $"Requires '{dep.Id}', which is not active."));
                    state = ActivationState.Inactive;
                }
                else if (d.State == ActivationState.ReadOnly && state == ActivationState.Active)
                {
                    reasons.Add(new("DEPENDENCY_READ_ONLY", $"Depends on '{dep.Id}', which is read-only."));
                    state = ActivationState.ReadOnly;
                }
            }

            foreach (var other in Conflicts(m, byId.Values, enabled))
            {
                reasons.Add(new("INCOMPATIBLE_COMBINATION", $"Cannot be active together with '{other}'."));
                state = ActivationState.Inactive;
            }

            visiting.Remove(id);
            return results[id] = new ModuleActivation(m, isEnabled, state, reasons);
        }

        foreach (var id in byId.Keys.Order(StringComparer.Ordinal))
        {
            Evaluate(id, new HashSet<string>(StringComparer.Ordinal));
        }

        return package.Select(m => results[m.Id]).ToList();
    }

    /// <summary>
    /// What enabling a module requires: the module plus any disabled non-platform dependencies (the
    /// closure). Licences are never enabled implicitly; missing ones are reported as problems.
    /// </summary>
    public static EnablePlan PlanEnable(
        IReadOnlyList<ModuleManifest> package,
        IReadOnlySet<string> enabled,
        Func<ModuleLicense, LicenseState> license,
        string moduleId)
    {
        ArgumentNullException.ThrowIfNull(package);
        var byId = package.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var problems = new List<ActivationReason>();
        if (!byId.TryGetValue(moduleId, out var target))
        {
            return new EnablePlan(false, [], [new("UNKNOWN_MODULE", $"Module '{moduleId}' is not installed in this deployment.")]);
        }

        if (target.Kind == ModuleKind.Platform)
        {
            return new EnablePlan(false, [], [new("PLATFORM_MODULE", "Platform modules are always active.")]);
        }

        var toEnable = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Visit(ModuleManifest m)
        {
            if (!seen.Add(m.Id))
            {
                return;
            }

            foreach (var dep in m.Dependencies)
            {
                if (!byId.TryGetValue(dep.Id, out var d))
                {
                    problems.Add(new("MISSING_DEPENDENCY", $"'{m.Id}' requires '{dep.Id}', which is not installed in this deployment."));
                    continue;
                }

                Visit(d);
            }

            if (m.Kind != ModuleKind.Platform && !enabled.Contains(m.Id))
            {
                toEnable.Add(m.Id);
            }

            if (m.Kind != ModuleKind.Platform && m.License is { } l && license(l) == LicenseState.None)
            {
                problems.Add(new("NOT_ENTITLED", $"'{m.Id}' needs a licence for {l.ProductCode}:{l.Feature}."));
            }
        }

        Visit(target);

        var after = new HashSet<string>(enabled, StringComparer.Ordinal);
        after.UnionWith(toEnable);
        foreach (var id in toEnable)
        {
            foreach (var other in Conflicts(byId[id], byId.Values, after))
            {
                problems.Add(new("INCOMPATIBLE_COMBINATION", $"'{id}' cannot be active together with '{other}'."));
            }
        }

        return new EnablePlan(problems.Count == 0, toEnable, problems.DistinctBy(p => p.Message).ToList());
    }

    /// <summary>Enabled modules that depend (directly or transitively) on the given module.</summary>
    public static IReadOnlyList<string> EnabledDependents(IReadOnlyList<ModuleManifest> package, IReadOnlySet<string> enabled, string moduleId)
    {
        ArgumentNullException.ThrowIfNull(package);
        var result = new SortedSet<string>(StringComparer.Ordinal);
        var frontier = new Queue<string>([moduleId]);
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var m in package.Where(m => enabled.Contains(m.Id) && m.Dependencies.Any(d => d.Id == current)))
            {
                if (result.Add(m.Id))
                {
                    frontier.Enqueue(m.Id);
                }
            }
        }

        return result.ToList();
    }

    private static IEnumerable<string> Conflicts(ModuleManifest m, IEnumerable<ModuleManifest> all, IReadOnlySet<string> enabled) =>
        all.Where(o => o.Id != m.Id && enabled.Contains(o.Id) && enabled.Contains(m.Id) &&
                       (m.IncompatibleWith.Contains(o.Id) || o.IncompatibleWith.Contains(m.Id)))
           .Select(o => o.Id)
           .Order(StringComparer.Ordinal);
}
