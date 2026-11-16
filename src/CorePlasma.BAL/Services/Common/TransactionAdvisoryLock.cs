using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CorePlasma.BAL.Services.Common;

internal static class TransactionAdvisoryLock
{
    public static Task AcquireAsync(DatabaseFacade database, long key, CancellationToken cancellationToken)
    {
        if (!database.IsSqlServer())
            return database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);

        var resource = $"CorePlasma:lock:{key:X16}";
        return database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive',
                @LockOwner = 'Transaction', @LockTimeout = -1;
            IF @result < 0 THROW 51000, 'Could not acquire the CorePlasma transaction lock.', 1;
            """, cancellationToken);
    }
}
