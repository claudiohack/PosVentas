using Microsoft.EntityFrameworkCore;
using PosVentas.Domain.Repositories;
using PosVentas.Infrastructure.Persistence.Contexts; // Importamos tu DbContext real

namespace PosVentas.Infrastructure.Persistence.Repositories;

// Cambiamos 'DbContext' por 'ApplicationDbContext' en el constructor primario
public class UnitOfWork(ApplicationDbContext context) : IUnitOfWork
{
    // Guardamos la referencia usando la clase base DbContext para mantener el resto del código intacto
    private readonly DbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await _context.SaveChangesAsync(cancellationToken);

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}