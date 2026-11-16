using CorePlasma.BAL.DTOs.Common;
using CorePlasma.Common.Enums;

namespace CorePlasma.BAL.DTOs.TankTransfers;

/// <summary>Phiếu điều chuyển bình khí — list row.</summary>
public class TankTransferDto : VoucherDto
{
    public Guid? ToCustomerId { get; set; }

    public string? ToCustomerName { get; set; }

    public Guid? ToEmployeeId { get; set; }

    public string? ToEmployeeName { get; set; }

    public string? ToLocation { get; set; }

    public TankStatus? ToStatus { get; set; }

    public int TankCount { get; set; }

    public bool SourceManaged { get; set; }

    public string? SourceSystem { get; set; }

    public string? SourceRecordId { get; set; }

    public string? SourceStatusCode { get; set; }

    public string? SourceStatusLabel { get; set; }
}

/// <summary>Phiếu điều chuyển bình khí — full voucher with tank lines.</summary>
public sealed class TankTransferDetailDto : VoucherDetailDto
{
    public bool SourceManaged { get; set; }

    public string? SourceSystem { get; set; }

    public string? SourceRecordId { get; set; }

    public string? SourceStatusCode { get; set; }

    public string? SourceStatusLabel { get; set; }

    public string? SourceDetailsJson { get; set; }

    public Guid? ToCustomerId { get; set; }

    public string? ToCustomerCode { get; set; }

    public string? ToCustomerName { get; set; }

    public Guid? ToEmployeeId { get; set; }

    public string? ToEmployeeCode { get; set; }

    public string? ToEmployeeName { get; set; }

    public string? ToLocation { get; set; }

    public TankStatus? ToStatus { get; set; }

    public List<TankTransferItemDto> Items { get; set; } = [];
}

public sealed class TankTransferItemDto
{
    public Guid Id { get; set; }

    public Guid TankId { get; set; }

    public string TankCode { get; set; } = string.Empty;

    /// <summary>Snapshot taken when the voucher is approved.</summary>
    public Guid? FromCustomerId { get; set; }

    public Guid? FromEmployeeId { get; set; }

    public string? FromLocation { get; set; }

    public TankStatus? FromStatus { get; set; }

    public string? Note { get; set; }
}

public abstract class TankTransferWriteDto : VoucherWriteDto
{
    /// <summary>Destination customer (null = unchanged).</summary>
    public Guid? ToCustomerId { get; set; }

    /// <summary>Destination employee in charge (null = unchanged).</summary>
    public Guid? ToEmployeeId { get; set; }

    /// <summary>Destination location (null = unchanged).</summary>
    public string? ToLocation { get; set; }

    /// <summary>Tank status after transfer (null = unchanged).</summary>
    public TankStatus? ToStatus { get; set; }

    public List<TankTransferItemInputDto> Items { get; set; } = [];
}

public sealed class TankTransferItemInputDto
{
    public Guid TankId { get; set; }

    public string? Note { get; set; }
}

public sealed class CreateTankTransferDto : TankTransferWriteDto;

public sealed class UpdateTankTransferDto : TankTransferWriteDto;

public sealed class TankTransferFilterDto : VoucherFilterDto
{
    public Guid? ToCustomerId { get; set; }

    public Guid? ToEmployeeId { get; set; }

    /// <summary>Vouchers containing this tank.</summary>
    public Guid? TankId { get; set; }
}
