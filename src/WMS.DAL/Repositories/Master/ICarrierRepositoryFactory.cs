namespace WMS.DAL.Repositories.Master;

public interface ICarrierRepositoryFactory
{
    ICarrierRepository For(Guid tenantId);
}
