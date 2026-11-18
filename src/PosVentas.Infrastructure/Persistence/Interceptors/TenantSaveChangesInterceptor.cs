using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Common;

namespace PosVentas.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Antes de guardar:
/// - asigna el TenantId actual a toda entidad nueva de tipo <see cref="ITenantEntity"/>;
/// - impide crear, modificar o borrar entidades de otro comercio, o cambiar su TenantId.
/// Si no hay tenant establecido, ninguna entidad del tenant puede guardarse.
/// </summary>
public sealed class TenantSaveChangesInterceptor(ICurrentTenantService currentTenant) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AplicarTenant(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AplicarTenant(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AplicarTenant(DbContext? context)
    {
        if (context is null) return;

        var entradas = context.ChangeTracker.Entries<ITenantEntity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (entradas.Count == 0) return;

        var tenantId = currentTenant.TenantId
            ?? throw new TenantAccessException("No hay un comercio establecido para guardar datos.");

        foreach (var entrada in entradas)
        {
            var propiedad = entrada.Property(e => e.TenantId);

            switch (entrada.State)
            {
                case EntityState.Added when entrada.Entity.TenantId == Guid.Empty:
                    entrada.Entity.TenantId = tenantId;
                    break;

                case EntityState.Added when entrada.Entity.TenantId != tenantId:
                    throw new TenantAccessException("No se pueden crear datos para otro comercio.");

                case EntityState.Modified or EntityState.Deleted
                    when propiedad.OriginalValue != tenantId || propiedad.CurrentValue != tenantId:
                    throw new TenantAccessException("No se pueden modificar datos de otro comercio.");
            }
        }
    }
}
