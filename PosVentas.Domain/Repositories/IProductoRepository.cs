using PosVentas.Domain.Entities;
using PosVentas.Domain.ValueObjects;

namespace PosVentas.Domain.Repositories;

public interface IProductoRepository : IBaseRepository<Producto, Guid>
{
    Task<Producto?> GetBySkuAsync(Sku sku, CancellationToken cancellationToken = default);
}