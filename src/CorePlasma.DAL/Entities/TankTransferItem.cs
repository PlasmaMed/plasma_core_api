using CorePlasma.Common.Enums;

namespace CorePlasma.DAL.Entities;

/// <summary>
/// One tank on a transfer voucher. "From*" columns are snapshotted at approval for history.
/// </summary>
public class TankTransferItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TankTransferId { get; set; }

    public Guid TankId { get; set; }

    /// <summary>
    /// Snapshot so the voucher stays readable even if the tank is renamed or deleted.
    /// </summary>
    public string TankCode { get; set; } = string.Empty;

    public Guid? FromCustomerId { get; set; }

    public Guid? FromEmployeeId { get; set; }

    public string? FromLocation { get; set; }

    public TankStatus? FromStatus { get; set; }

    public string? Note { get; set; }

    public TankTransfer TankTransfer { get; set; } = null!;

    public Tank Tank { get; set; } = null!;
}
