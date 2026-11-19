using HealthOS.Modules.Sdk.Composition;

namespace HealthOS.ArchitectureTests;

public class ArchitectureTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HealthOS.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (HealthOS.slnx) not found.");
    }

    private static string Fixture(string name) =>
        Path.Combine(RepoRoot(), "tests", "architecture", "HealthOS.ArchitectureTests", "Fixtures", name);

    [Fact]
    public void Repository_respects_module_reference_rules()
    {
        var violations = ReferenceRules.Check(RepoRoot());
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void All_repository_manifests_are_valid_and_resolve_together()
    {
        var manifests = ReferenceRules.LoadManifests(RepoRoot()).Values.ToList();
        Assert.NotEmpty(manifests);

        var resolution = PackageResolver.Resolve(manifests, "0.1.0");
        Assert.True(resolution.Succeeded, string.Join(Environment.NewLine, resolution.Issues));
    }

    [Fact]
    public void Checker_detects_reference_to_another_modules_internal_project()
    {
        var violations = ReferenceRules.Check(Fixture("internal-reference"));
        Assert.Contains(violations, v => v.Contains("Careops.Quality -> Clinicos.Core", StringComparison.Ordinal) &&
                                         v.Contains("internal project", StringComparison.Ordinal));
    }

    [Fact]
    public void Checker_detects_contracts_reference_without_declared_dependency()
    {
        var violations = ReferenceRules.Check(Fixture("undeclared-dependency"));
        Assert.Contains(violations, v => v.Contains("not a declared dependency", StringComparison.Ordinal));
    }
}
