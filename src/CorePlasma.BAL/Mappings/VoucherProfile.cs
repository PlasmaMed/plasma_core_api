using AutoMapper;
using CorePlasma.BAL.DTOs.DocumentSequences;
using CorePlasma.BAL.DTOs.TankTransfers;
using CorePlasma.DAL.Entities;

namespace CorePlasma.BAL.Mappings;

public sealed class VoucherProfile : Profile
{
    public VoucherProfile()
    {
        CreateMap<TankTransfer, TankTransferDto>()
            .ForMember(d => d.ToCustomerName, o => o.MapFrom(s => s.ToCustomer != null ? s.ToCustomer.CustomerName : null))
            .ForMember(d => d.ToEmployeeName, o => o.MapFrom(s => s.ToEmployee != null ? s.ToEmployee.FullName : null))
            .ForMember(d => d.TankCount, o => o.MapFrom(s => s.Items.Count));

        CreateMap<TankTransfer, TankTransferDetailDto>()
            .ForMember(d => d.ToCustomerCode, o => o.MapFrom(s => s.ToCustomer != null ? s.ToCustomer.CustomerCode : null))
            .ForMember(d => d.ToCustomerName, o => o.MapFrom(s => s.ToCustomer != null ? s.ToCustomer.CustomerName : null))
            .ForMember(d => d.ToEmployeeCode, o => o.MapFrom(s => s.ToEmployee != null ? s.ToEmployee.EmployeeCode : null))
            .ForMember(d => d.ToEmployeeName, o => o.MapFrom(s => s.ToEmployee != null ? s.ToEmployee.FullName : null))
            .ForMember(d => d.Items, o => o.MapFrom(s => s.Items.OrderBy(i => i.TankCode)));

        CreateMap<TankTransferItem, TankTransferItemDto>();

        // Header only: VoucherNo/Status are system-managed, items are rebuilt by the service.
        CreateMap<CreateTankTransferDto, TankTransfer>()
            .ForMember(d => d.Items, o => o.Ignore())
            .ForMember(d => d.VoucherDate, o => o.Ignore());
        CreateMap<UpdateTankTransferDto, TankTransfer>()
            .ForMember(d => d.Items, o => o.Ignore());

        CreateMap<DocumentSequence, DocumentSequenceDto>()
            .ForMember(d => d.NextNumber, o => o.Ignore());
        CreateMap<UpdateDocumentSequenceDto, DocumentSequence>();
    }
}
