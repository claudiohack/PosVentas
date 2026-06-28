using Microsoft.EntityFrameworkCore;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Repositories;
using PosVentas.Domain.ValueObjects;

namespace PosVentas.Infrastructure.Persistence.Repositories;

public class ProductoRepository(DbContext context) : BaseRepository<Producto, Guid>(context), IProductoRepository
{
    public async Task<Producto?> GetBySkuAsync(Sku sku, CancellationToken cancellationToken = default)
    {
        if (sku is null) return null;
        return await DbSet.Include(p => p.Categoria).FirstOrDefaultAsync(p => p.Sku == sku, cancellationToken);
    }
}