using CorePlasma.DAL.Entities;

namespace CorePlasma.DAL.Interfaces;

public interface IDocumentSequenceRepository : IRepository<DocumentSequence>
{
    /// <summary>
    /// Atomically increments the counter (restarting at 1 when <paramref name="period"/> changes) and returns
    /// the new value, or null when the sequence does not exist / is inactive. Safe under concurrent requests;
    /// joins the current transaction when one is open.
    /// </summary>
    Task<long?> IncrementAsync(string sequenceCode, string period, CancellationToken cancellationToken = default);
}
