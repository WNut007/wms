namespace WMS.DAL.Repositories.Master;

public interface IOwnerRepositoryFactory
{
    IOwnerRepository For(Guid tenantId);
}
