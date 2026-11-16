namespace CorePlasma.BAL.DTOs.Customers;

public class CustomerDetailDto : CustomerDto
{
    public string? Address { get; set; }

    public string? ContactPhone { get; set; }

    public string? ContactEmail { get; set; }

    public string? Website { get; set; }

    public string? Description { get; set; }

    public int TankCount { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public Guid? UpdatedBy { get; set; }
}
