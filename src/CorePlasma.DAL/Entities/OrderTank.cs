namespace CorePlasma.DAL.Entities;

/// <summary>A tank of an order (PLT Invoice.tanks).</summary>
public class OrderTank : BaseEntity
{
    public Guid OrderId { get; set; }

    /// <summary>Null when the PLT tank id has no tank in this system.</summary>
    public Guid? TankId { get; set; }

    /// <summary>PLT ObjectId of the tank (kept even when TankId is null).</summary>
    public string? TankLegacyId { get; set; }

    /// <summary>Position in the PLT list.</summary>
    public int SortOrder { get; set; }

    public Order Order { get; set; } = null!;

    public Tank? Tank { get; set; }
}
