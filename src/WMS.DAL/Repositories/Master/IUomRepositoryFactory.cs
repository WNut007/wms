namespace WMS.DAL.Repositories.Master;

public interface IUomRepositoryFactory
{
    IUomRepository For(Guid tenantId);
}
