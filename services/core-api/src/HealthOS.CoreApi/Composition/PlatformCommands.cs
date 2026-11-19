using HealthOS.Core.Identity.Data;
using HealthOS.Modules.Sdk.Context;

namespace HealthOS.CoreApi.Composition;

/// <summary>
/// Operator commands run instead of the web server:
///   dotnet HealthOS.CoreApi.dll platform grant-operator &lt;email&gt;
/// Appoints a platform operator. The person must have signed in once (so their account exists).
/// This is the only way to create the first operator; it requires database access, not an API call.
/// </summary>
public static class PlatformCommands
{
    public static bool IsPlatformCommand(string[] args) => args.Length >= 2 && args[0] == "platform";

    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        ArgumentNullException.ThrowIfNull(app);
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("HealthOS.Platform");

        if (args is not [_, "grant-operator", var email])
        {
            logger.LogError("Usage: platform grant-operator <email>");
            return 2;
        }

        await using var scope = app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().EnterPlatformScope();
        var access = scope.ServiceProvider.GetRequiredService<IOperatorAdministration>();
        return await access.GrantOperatorByEmailAsync(email, CancellationToken.None).ConfigureAwait(false) ? 0 : 1;
    }
}
