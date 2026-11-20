using Microsoft.EntityFrameworkCore;
using PosVentas.Domain.Entities;

namespace PosVentas.Application.Common.Interfaces;

/// <summary>
/// Puerto de persistencia. Todas las entidades <c>ITenantEntity</c> ya vienen filtradas
/// por el comercio actual (filtro global de EF Core); no hace falta filtrar por TenantId a mano.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Comercio> Comercios { get; }
    DbSet<Usuario> Usuarios { get; }
    DbSet<Categoria> Categorias { get; }
    DbSet<Producto> Productos { get; }
    DbSet<Cliente> Clientes { get; }
    DbSet<Caja> Cajas { get; }
    DbSet<CajaSesion> CajaSesiones { get; }
    DbSet<Venta> Ventas { get; }
    DbSet<VentaDetalle> VentaDetalles { get; }
    DbSet<VentaPago> VentaPagos { get; }
    DbSet<MovimientoCaja> MovimientosCaja { get; }
    DbSet<MovimientoCuentaCorriente> MovimientosCuentaCorriente { get; }
    DbSet<ReciboCobranza> RecibosCobranza { get; }
    DbSet<Proveedor> Proveedores { get; }
    DbSet<Compra> Compras { get; }
    DbSet<CompraDetalle> CompraDetalles { get; }
    DbSet<ComprobanteFiscal> ComprobantesFiscales { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
