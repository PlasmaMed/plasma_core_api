namespace CorePlasma.DAL.Entities;

/// <summary>
/// Entity whose Status switches to "Deleted" when it is soft deleted, so the state is visible in the data itself.
/// </summary>
public interface IHasDeletedStatus
{
    void MarkAsDeleted();
}
