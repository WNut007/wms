namespace WMS.DAL.Repositories.Master;

public interface IZoneRepositoryFactory
{
    IZoneRepository For(Guid tenantId);
}
