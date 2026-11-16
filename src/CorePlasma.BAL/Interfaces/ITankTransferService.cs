using CorePlasma.BAL.DTOs.TankTransfers;

namespace CorePlasma.BAL.Interfaces;

/// <summary>Phiếu điều chuyển bình khí.</summary>
public interface ITankTransferService
    : IVoucherService<TankTransferDto, TankTransferDetailDto, CreateTankTransferDto, UpdateTankTransferDto, TankTransferFilterDto>;
