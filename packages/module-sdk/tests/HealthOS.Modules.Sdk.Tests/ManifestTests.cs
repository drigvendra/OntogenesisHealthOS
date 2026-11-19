using HealthOS.Modules.Sdk.Manifest;

namespace HealthOS.Modules.Sdk.Tests;

public class ManifestReaderTests
{
    private const string ValidJson = """
        {
          "$schema": "../../packages/module-sdk/schema/module-manifest.v1.schema.json",
          "id": "careops.quality",
          "kind": "product",
          "version": "1.0.0",
          "displayName": "CareOps Quality",
          "requiresCore": ">=1.0.0 <2.0.0",
          "dependencies": [ { "id": "core.documents", "version": "^1.0.0" } ],
          "license": { "productCode": "CAREOPS", "feature": "quality.core" },
          "permissions": [
            { "code": "careops.document.read", "description": "Read" },
            { "code": "careops.document.approve", "description": "Approve", "requiresESignature": true }
          ],
          "database": { "schema": "careops_quality" },
          "ui": { "routes": [ { "path": "/quality/documents", "app": "quality-web", "permission": "careops.document.read" } ] }
        }
        """;

    [Fact]
    public void Parses_valid_manifest()
    {
        var m = ModuleManifestReader.Parse(ValidJson);

        Assert.Equal("careops.quality", m.Id);
        Assert.Equal(ModuleKind.Product, m.Kind);
        Assert.Single(m.Dependencies);
        Assert.True(m.Permissions[1].RequiresESignature);
        Assert.Equal("careops_quality", m.Database!.Schema);
        Assert.Empty(ManifestValidator.Validate(m));
    }

    [Fact]
    public void Rejects_unknown_properties()
    {
        var json = ValidJson.Replace("\"displayName\"", "\"displayNmae\"", StringComparison.Ordinal);
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ModuleManifestReader.Parse(json));
    }
}

public class ManifestValidatorTests
{
    private static IEnumerable<string> Codes(ModuleManifest m) => ManifestValidator.Validate(m).Select(i => i.Code);

    [Theory]
    [InlineData("Careops.Quality")]
    [InlineData("careops")]
    [InlineData("careops..quality")]
    [InlineData("1careops.quality")]
    public void Rejects_invalid_ids(string id) =>
        Assert.Contains("INVALID_ID", Codes(TestManifests.Product(id)));

    [Fact]
    public void Rejects_non_semver_version() =>
        Assert.Contains("INVALID_VERSION", Codes(TestManifests.Make("careops.quality", ModuleKind.Product, version: "1.0")));

    [Fact]
    public void Rejects_invalid_core_range() =>
        Assert.Contains("INVALID_RANGE", Codes(TestManifests.Make("careops.quality", ModuleKind.Product, requiresCore: "not a range")));

    [Fact]
    public void Product_requires_license() =>
        Assert.Contains("MISSING_LICENSE", Codes(TestManifests.Product("careops.quality") with { License = null }));

    [Fact]
    public void Platform_must_not_have_license() =>
        Assert.Contains("UNEXPECTED_LICENSE", Codes(TestManifests.Platform("core.audit") with { License = new("X", "y") }));

    [Fact]
    public void Permissions_must_use_module_namespace()
    {
        var m = TestManifests.Product("careops.quality") with
        {
            Permissions = [new PermissionDeclaration("clinicos.patient.read", "wrong namespace")],
        };
        Assert.Contains("PERMISSION_NAMESPACE", Codes(m));
    }

    [Fact]
    public void Ui_route_must_use_declared_permission()
    {
        var m = TestManifests.Product("careops.quality") with
        {
            Ui = new ModuleUi { Routes = [new UiRoute("/q", "quality-web", "careops.document.read")] },
        };
        Assert.Contains("UNKNOWN_ROUTE_PERMISSION", Codes(m));
    }

    [Theory]
    [InlineData("public")]
    [InlineData("pg_catalog")]
    [InlineData("Bad-Schema")]
    public void Rejects_reserved_or_invalid_schema(string schema) =>
        Assert.Contains("INVALID_SCHEMA", Codes(TestManifests.Make("careops.quality", ModuleKind.Product, schema: schema)));

    [Fact]
    public void Destructive_uninstall_is_not_permitted()
    {
        var m = TestManifests.Product("careops.quality") with { DataRetention = new ModuleDataRetention { DestructiveUninstall = true } };
        Assert.Contains("DESTRUCTIVE_UNINSTALL", Codes(m));
    }

    [Fact]
    public void Self_dependency_rejected() =>
        Assert.Contains("SELF_DEPENDENCY", Codes(TestManifests.Product("careops.quality", ("careops.quality", "^1.0.0"))));
}
