using HealthOS.Modules.Sdk.Compliance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace HealthOS.Modules.Sdk.Tests;

public static partial class RedactionCanaryLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Registered patient {PatientName} with mobile {Mobile} at site {SiteCode}")]
    public static partial void PatientRegistered(ILogger logger, [Phi] string patientName, [Pii] string mobile, string siteCode);
}

/// <summary>
/// PHI canary: synthetic identifiers tagged with data classifications must never reach log output.
/// </summary>
public class RedactionTests
{
    private const string CanaryName = "ZZCANARY-Synthetic Patient";
    private const string CanaryMobile = "+91-00000-CANARY";

    [Fact]
    public void Classified_values_are_erased_from_logs()
    {
        using var sp = new ServiceCollection()
            .AddHealthOsRedaction()
            .AddLogging(b => b.EnableRedaction().AddFakeLogging())
            .BuildServiceProvider();

        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("canary");
        RedactionCanaryLog.PatientRegistered(logger, CanaryName, CanaryMobile, "SITE-01");

        var record = Assert.Single(sp.GetFakeLogCollector().GetSnapshot());
        var everything = record.Message + " " + string.Join(" ", record.StructuredState?.Select(kv => $"{kv.Key}={kv.Value}") ?? []);

        Assert.DoesNotContain("CANARY", everything, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SITE-01", everything, StringComparison.Ordinal);
    }
}
