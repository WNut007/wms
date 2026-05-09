namespace WMS.DAL.Repositories.Master;

public interface IProductCategoryRepositoryFactory
{
    IProductCategoryRepository For(Guid tenantId);
}
