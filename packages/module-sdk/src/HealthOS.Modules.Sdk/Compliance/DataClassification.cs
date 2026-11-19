using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;

namespace HealthOS.Modules.Sdk.Compliance;

/// <summary>
/// HealthOS data classification taxonomy. Values tagged with these classifications are erased
/// from logs by the redaction pipeline (see <see cref="RedactionServiceCollectionExtensions"/>).
/// </summary>
public static class HealthOsTaxonomy
{
    public const string Name = "HealthOS";

    /// <summary>Protected health information: anything about a patient's health or identity.</summary>
    public static DataClassification Phi => new(Name, nameof(Phi));

    /// <summary>Personal data that is not health data (staff names, emails, phone numbers).</summary>
    public static DataClassification Pii => new(Name, nameof(Pii));

    /// <summary>Secrets and credentials.</summary>
    public static DataClassification Secret => new(Name, nameof(Secret));
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
public sealed class PhiAttribute : DataClassificationAttribute
{
    public PhiAttribute()
        : base(HealthOsTaxonomy.Phi)
    {
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
public sealed class PiiAttribute : DataClassificationAttribute
{
    public PiiAttribute()
        : base(HealthOsTaxonomy.Pii)
    {
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
public sealed class SecretAttribute : DataClassificationAttribute
{
    public SecretAttribute()
        : base(HealthOsTaxonomy.Secret)
    {
    }
}

public static class HealthOsRedactionExtensions
{
    /// <summary>Erases PHI, PII and secrets entirely (no hashing: hashes of low-entropy identifiers are reversible).</summary>
    public static IServiceCollection AddHealthOsRedaction(this IServiceCollection services)
    {
        return services.AddRedaction(b =>
        {
            b.SetRedactor<ErasingRedactor>(
                new DataClassificationSet(HealthOsTaxonomy.Phi),
                new DataClassificationSet(HealthOsTaxonomy.Pii),
                new DataClassificationSet(HealthOsTaxonomy.Secret));
        });
    }
}
