namespace WMS.DAL.Repositories.Master;

public interface IBoxTypeRepositoryFactory
{
    IBoxTypeRepository For(Guid tenantId);
}
