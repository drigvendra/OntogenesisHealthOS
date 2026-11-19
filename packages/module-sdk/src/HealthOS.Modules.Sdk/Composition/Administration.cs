using System.Text.Json;

namespace HealthOS.Modules.Sdk.Composition;

/// <summary>
/// Platform administration contracts used by onboarding. Each is implemented by the module that owns the
/// data (licensing, modules, identity, configuration), so onboarding never touches another module's tables.
/// All calls run in platform scope and are audited by the implementing module.
/// </summary>
public interface ILicenseAdministration
{
    /// <summary>Grants a feature unless the tenant already has an entitlement for it.</summary>
    Task GrantIfMissingAsync(Guid tenantId, string feature, DateTimeOffset? validTo, string source, CancellationToken cancellationToken);
}

public sealed record EnableResult(bool Succeeded, IReadOnlyList<string> Enabled, IReadOnlyList<string> Problems);

public interface IModuleAdministration
{
    Task<EnableResult> EnableAsync(Guid tenantId, string moduleId, bool includeDependencies, CancellationToken cancellationToken);
}

public sealed record RoleTemplate(string Code, string Name, IReadOnlyList<string> Permissions);

public interface IRoleAdministration
{
    /// <summary>Creates or updates a tenant role. Returns problems (e.g. unknown or platform permissions) instead of saving.</summary>
    Task<IReadOnlyList<string>> EnsureRoleAsync(Guid tenantId, RoleTemplate role, CancellationToken cancellationToken);
}

public interface IConfigurationAdministration
{
    Task<IReadOnlyList<string>> SetProfileAsync(Guid tenantId, string country, string industry, string? specialty, CancellationToken cancellationToken);

    /// <summary>Publishes a form or workflow definition ("form" | "workflow"). Returns problems instead when invalid.</summary>
    Task<IReadOnlyList<string>> PublishDefinitionAsync(Guid tenantId, string kind, JsonElement document, string source, CancellationToken cancellationToken);
}
