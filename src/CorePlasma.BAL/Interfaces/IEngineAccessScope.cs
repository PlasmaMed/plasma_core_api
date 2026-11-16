namespace CorePlasma.BAL.Interfaces;

/// <summary>
/// Row-level access to machines: employees without ENGINE_VIEW_ALL (e.g. sales) only see the machines
/// they are responsible for (Engine.ResponsibleEmployeeId). Also applies to the machines' session history.
/// </summary>
public interface IEngineAccessScope
{
    /// <summary>The employee the caller is restricted to, or null when the caller may see every machine.</summary>
    Task<Guid?> GetRestrictedEmployeeIdAsync(CancellationToken cancellationToken);
}
