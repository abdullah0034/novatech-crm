using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Repositories;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Order?> GetByFraudCheckIdAsync(string fraudCheckId, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetByCustomerAsync(string customerId, CancellationToken ct = default);
    Task SaveAsync(Order order, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
