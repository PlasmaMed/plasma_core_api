using CorePlasma.BAL.DTOs.Engines;

namespace CorePlasma.BAL.Interfaces;

public interface IEngineService
    : ICrudService<EngineDto, EngineDetailDto, CreateEngineDto, UpdateEngineDto, EngineFilterDto>;
