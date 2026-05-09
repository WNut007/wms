namespace WMS.DAL.Repositories.Master;

public interface ILocationRepositoryFactory
{
    ILocationRepository For(Guid tenantId);
}
