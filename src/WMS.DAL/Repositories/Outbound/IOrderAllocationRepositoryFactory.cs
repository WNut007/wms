namespace WMS.DAL.Repositories.Outbound;

public interface IOrderAllocationRepositoryFactory
{
    IOrderAllocationRepository For(Guid tenantId);
}
