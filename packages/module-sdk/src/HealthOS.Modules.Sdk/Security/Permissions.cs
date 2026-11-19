using System.Security.Claims;
using HealthOS.Modules.Sdk.Manifest;

namespace HealthOS.Modules.Sdk.Security;

/// <summary>Decides whether the current principal holds a permission in the current request context.</summary>
public interface IPermissionEvaluator
{
    Task<PermissionDecision> EvaluateAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken);
}

public sealed record PermissionDecision(bool Granted, string Reason)
{
    public static PermissionDecision Allow(string reason) => new(true, reason);

    public static PermissionDecision Deny(string reason) => new(false, reason);
}

/// <summary>All permissions declared by modules in this package, keyed by code.</summary>
public sealed class PermissionCatalog
{
    private readonly Dictionary<string, (string ModuleId, PermissionDeclaration Declaration)> _byCode;

    public PermissionCatalog(IEnumerable<ModuleManifest> manifests)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        _byCode = new Dictionary<string, (string, PermissionDeclaration)>(StringComparer.Ordinal);
        foreach (var m in manifests)
        {
            foreach (var p in m.Permissions)
            {
                if (!_byCode.TryAdd(p.Code, (m.Id, p)))
                {
                    throw new InvalidOperationException($"Permission '{p.Code}' is declared by more than one module.");
                }
            }
        }
    }

    public PermissionDeclaration? Find(string code) => _byCode.TryGetValue(code, out var v) ? v.Declaration : null;

    public IEnumerable<PermissionDeclaration> All => _byCode.Values.Select(v => v.Declaration);

    public IEnumerable<PermissionDeclaration> TenantScoped => All.Where(p => p.Scope == PermissionScope.Tenant);
}

public static class HealthOsClaims
{
    /// <summary>Internal HealthOS user id.</summary>
    public const string UserId = "hos_uid";

    /// <summary>Active tenant selected for this session (validated against membership on every request).</summary>
    public const string TenantId = "hos_tid";

    private static readonly HashSet<string> MultiFactorMethods = ["mfa", "otp", "hwk", "swk", "sms", "fido", "webauthn"];

    /// <summary>True when the OIDC "amr" claims show a second factor (RFC 8176).</summary>
    public static bool HasMultiFactor(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.FindAll("amr").Any(c => MultiFactorMethods.Contains(c.Value));
    }
}
