namespace HealthOS.Modules.Sdk.Composition;

/// <summary>Activation of installed modules for the active tenant of the current request.</summary>
public interface IModuleActivation
{
    Task<IReadOnlyList<ModuleActivation>> GetAllAsync(CancellationToken cancellationToken);

    Task<ModuleActivation?> GetAsync(string moduleId, CancellationToken cancellationToken);
}
