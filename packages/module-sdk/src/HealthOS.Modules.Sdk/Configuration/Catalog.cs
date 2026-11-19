using HealthOS.Modules.Sdk.Manifest;

namespace HealthOS.Modules.Sdk.Configuration;

/// <summary>All settings declared by modules installed in this package.</summary>
public sealed class SettingsCatalog
{
    private readonly Dictionary<string, SettingDefinition> _byKey;

    public SettingsCatalog(IEnumerable<ModuleManifest> manifests)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        _byKey = new Dictionary<string, SettingDefinition>(StringComparer.Ordinal);
        foreach (var s in manifests.SelectMany(m => m.Settings))
        {
            if (!_byKey.TryAdd(s.Key, s))
            {
                throw new InvalidOperationException($"Setting '{s.Key}' is declared by more than one module.");
            }
        }
    }

    public SettingDefinition? Find(string key) => _byKey.GetValueOrDefault(key);

    public IEnumerable<SettingDefinition> All => _byKey.Values.OrderBy(s => s.Key, StringComparer.Ordinal);
}

/// <summary>Role codes of a user in the active tenant, in priority order (implemented by core.identity).</summary>
public interface IUserRoles
{
    Task<IReadOnlyList<string>> GetRoleCodesAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Effective setting values for the current request's user and tenant (implemented by core.configuration).</summary>
public interface ISettingsReader
{
    Task<ResolvedSetting> GetAsync(string key, string? siteId, CancellationToken cancellationToken);
}
