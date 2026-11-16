using CorePlasma.DAL.Context;
using CorePlasma.DAL.Entities;
using CorePlasma.DAL.Interfaces;
using CorePlasma.DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CorePlasma.DAL.UnitOfWork;

public sealed class UnitOfWork(ApplicationDbContext context) : IUnitOfWork
{
    public bool IsSqlServer => context.Database.IsSqlServer();

    private IDbContextTransaction? _transaction;

    private IRepository<Customer>? _customers;
    private IRepository<Employee>? _employees;
    private IRepository<Tank>? _tanks;
    private IRepository<Engine>? _engines;
    private IRepository<Permission>? _permissions;
    private IRepository<EmployeePermission>? _employeePermissions;
    private IRepository<PermissionAuditLog>? _permissionAuditLogs;
    private IRepository<DataChangeLog>? _dataChangeLogs;
    private IRepository<Session>? _sessions;
    private IRepository<TankSession>? _tankSessions;
    private IRepository<TankEngineHistory>? _tankEngineHistories;
    private IRepository<Order>? _orders;
    private IRepository<OrderTank>? _orderTanks;
    private IRepository<EmployeeUser>? _employeeUsers;
    private IRepository<CustomerUser>? _customerUsers;
    private IDocumentSequenceRepository? _documentSequences;
    private IRepository<TankTransfer>? _tankTransfers;
    private IRepository<TankTransferItem>? _tankTransferItems;

    public IRepository<Customer> Customers => _customers ??= new Repository<Customer>(context);

    public IRepository<Employee> Employees => _employees ??= new Repository<Employee>(context);

    public IRepository<Tank> Tanks => _tanks ??= new Repository<Tank>(context);

    public IRepository<Engine> Engines => _engines ??= new Repository<Engine>(context);

    public IRepository<Permission> Permissions => _permissions ??= new Repository<Permission>(context);

    public IRepository<EmployeePermission> EmployeePermissions =>
        _employeePermissions ??= new Repository<EmployeePermission>(context);

    public IRepository<PermissionAuditLog> PermissionAuditLogs =>
        _permissionAuditLogs ??= new Repository<PermissionAuditLog>(context);

    public IRepository<DataChangeLog> DataChangeLogs => _dataChangeLogs ??= new Repository<DataChangeLog>(context);

    public IRepository<Session> Sessions => _sessions ??= new Repository<Session>(context);

    public IRepository<TankSession> TankSessions => _tankSessions ??= new Repository<TankSession>(context);

    public IRepository<TankEngineHistory> TankEngineHistories => _tankEngineHistories ??= new Repository<TankEngineHistory>(context);

    public IRepository<Order> Orders => _orders ??= new Repository<Order>(context);

    public IRepository<OrderTank> OrderTanks => _orderTanks ??= new Repository<OrderTank>(context);

    public IRepository<EmployeeUser> EmployeeUsers => _employeeUsers ??= new Repository<EmployeeUser>(context);

    public IRepository<CustomerUser> CustomerUsers => _customerUsers ??= new Repository<CustomerUser>(context);

    public IDocumentSequenceRepository DocumentSequences =>
        _documentSequences ??= new DocumentSequenceRepository(context);

    public IRepository<TankTransfer> TankTransfers => _tankTransfers ??= new Repository<TankTransfer>(context);

    public IRepository<TankTransferItem> TankTransferItems =>
        _tankTransferItems ??= new Repository<TankTransferItem>(context);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already in progress.");
        }

        _transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException("No transaction is in progress.");
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await _transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            return;
        }

        try
        {
            await _transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            context.ChangeTracker.Clear();
            await DisposeTransactionAsync();
        }
    }

    public async ValueTask DisposeAsync() => await DisposeTransactionAsync();

    private async Task DisposeTransactionAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
}
