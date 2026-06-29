using Microsoft.EntityFrameworkCore;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Repositories;
using PosVentas.Domain.ValueObjects;
using PosVentas.Infrastructure.Persistence.Contexts; // Aseguramos que conozca tu DbContext

namespace PosVentas.Infrastructure.Persistence.Repositories;

// Cambiamos 'DbContext' por 'ApplicationDbContext' en la entrada del constructor primario
public class ProductoRepository(ApplicationDbContext context)
    : BaseRepository<Producto, Guid>(context), IProductoRepository
{
    public async Task<Producto?> GetBySkuAsync(Sku sku, CancellationToken cancellationToken = default)
    {
        if (sku is null) return null;

        return await DbSet
            .Include(p => p.Categoria)
            .FirstOrDefaultAsync(p => p.Sku == sku, cancellationToken);
    }
}