namespace WMS.DAL.Repositories.Inbound;

public interface IReceivingHeaderRepositoryFactory
{
    IReceivingHeaderRepository For(Guid tenantId);
}
