using CorePlasma.DAL.Entities;

namespace CorePlasma.DAL.Interfaces;

public interface IUnitOfWork : IAsyncDisposable
{
    bool IsSqlServer { get; }

    IRepository<Customer> Customers { get; }

    IRepository<Employee> Employees { get; }

    IRepository<Tank> Tanks { get; }

    IRepository<Engine> Engines { get; }

    IRepository<Permission> Permissions { get; }

    IRepository<EmployeePermission> EmployeePermissions { get; }

    IRepository<PermissionAuditLog> PermissionAuditLogs { get; }

    IRepository<DataChangeLog> DataChangeLogs { get; }

    IRepository<Session> Sessions { get; }

    IRepository<TankSession> TankSessions { get; }

    IRepository<TankEngineHistory> TankEngineHistories { get; }

    IRepository<Order> Orders { get; }

    IRepository<OrderTank> OrderTanks { get; }

    IRepository<EmployeeUser> EmployeeUsers { get; }

    IRepository<CustomerUser> CustomerUsers { get; }

    IDocumentSequenceRepository DocumentSequences { get; }

    IRepository<TankTransfer> TankTransfers { get; }

    IRepository<TankTransferItem> TankTransferItems { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
