using HealthOS.Platform.Data;

namespace HealthOS.CoreApi.Composition;

/// <summary>
/// Operational commands run instead of the web server:
///   dotnet HealthOS.CoreApi.dll db bootstrap   (admin connection: database + login roles)
///   dotnet HealthOS.CoreApi.dll db migrate     (migrator connection: module schemas, roles, migrations)
/// </summary>
public static class DatabaseCommands
{
    public static bool IsDatabaseCommand(string[] args) => args.Length >= 2 && args[0] == "db";

    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        ArgumentNullException.ThrowIfNull(app);
        var config = app.Configuration;
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("HealthOS.Database");
        // Built from configuration directly: these commands must work without the runtime connection string.
        var roles = new DatabaseRoles(config["Database:RolePrefix"] ?? "hos");

        switch (args[1])
        {
            case "bootstrap":
                await DatabaseBootstrapper.BootstrapAsync(
                    Required(config, "Database:AdminConnectionString"),
                    config["Database:Name"] ?? "healthos",
                    roles,
                    Required(config, "Database:MigratorPassword"),
                    Required(config, "Database:AppPassword")).ConfigureAwait(false);
                logger.LogInformation("Database bootstrap completed.");
                return 0;

            case "migrate":
                var modules = app.Services.GetServices<IModuleMigrations>().ToList();
                await ModuleMigrator.MigrateAllAsync(Required(config, "Database:MigratorConnectionString"), roles, modules).ConfigureAwait(false);
                logger.LogInformation("Migrated {ModuleCount} module database(s).", modules.Count);
                return 0;

            default:
                logger.LogError("Unknown database command. Use 'db bootstrap' or 'db migrate'.");
                return 2;
        }
    }

    private static string Required(IConfiguration config, string key) =>
        config[key] is { Length: > 0 } v ? v : throw new InvalidOperationException($"Configuration value '{key}' is required.");
}
