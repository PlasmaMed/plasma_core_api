using CorePlasma.Common.Enums;

namespace CorePlasma.DAL.Entities;

/// <summary>
/// Phiếu điều chuyển bình khí: moves tanks to a customer / employee / location / status when approved.
/// </summary>
public class TankTransfer : VoucherBase
{
    /// <summary>Source-owned workflow; external delivery is not a Core approval command.</summary>
    public bool SourceManaged { get; set; }
    public string? SourceSystem { get; set; }
    public string? SourceRecordId { get; set; }
    public string? SourceStatusCode { get; set; }
    public string? SourceStatusLabel { get; set; }
    public string? SourceDetailsJson { get; set; }
    public Guid? ToCustomerId { get; set; }

    public Guid? ToEmployeeId { get; set; }

    public string? ToLocation { get; set; }

    public TankStatus? ToStatus { get; set; }

    public Customer? ToCustomer { get; set; }

    public Employee? ToEmployee { get; set; }

    public ICollection<TankTransferItem> Items { get; set; } = new List<TankTransferItem>();
}
