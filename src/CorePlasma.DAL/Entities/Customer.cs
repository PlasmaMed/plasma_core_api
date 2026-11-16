using CorePlasma.Common.Enums;

namespace CorePlasma.DAL.Entities;

public class Customer : BaseEntity, IHasDeletedStatus, ISourceIdentified
{
    /// <summary>Representative numeric source ID; all verified aliases remain in LegacySyncMappings.</summary>
    public long? QuanLyId { get; set; }

    public long? NewAppId { get; set; }

    public string CustomerCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string? ShortName { get; set; }

    public string? TaxCode { get; set; }

    public string? Address { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? ContactPerson { get; set; }

    public string? ContactPhone { get; set; }

    public string? ContactEmail { get; set; }

    public string? Website { get; set; }

    public string? Description { get; set; }

    public CustomerStatus Status { get; set; } = CustomerStatus.Active;

    public ICollection<Tank> Tanks { get; set; } = new List<Tank>();

    /// <summary>
    /// Login accounts of this customer's contacts (table CustomerUsers).
    /// </summary>
    public ICollection<CustomerUser> Users { get; set; } = new List<CustomerUser>();

    public void MarkAsDeleted() => Status = CustomerStatus.Deleted;
}
