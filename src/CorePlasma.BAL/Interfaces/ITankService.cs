using CorePlasma.BAL.DTOs.Tanks;

namespace CorePlasma.BAL.Interfaces;

public interface ITankService
    : ICrudService<TankDto, TankDetailDto, CreateTankDto, UpdateTankDto, TankFilterDto>;
