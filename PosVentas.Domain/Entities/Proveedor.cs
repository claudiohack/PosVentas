using System;
using PosVentas.Domain.Common;

namespace PosVentas.Domain.Entities;

public class Proveedor : Entity<Guid>
{
    // --- DATOS IDENTIFICATORIOS Y FISCALES ---
    public string RazonSocial { get; private set; } = string.Empty;
    public string? Cuit { get; private set; }
    public string TipoResponsabilidadIva { get; private set; } = "Responsable Inscripto";

    // --- DATOS DE CONTACTO ---
    public string? Telefono { get; private set; }
    public string? Email { get; private set; }
    public string? Direccion { get; private set; }
    public string? NombreContacto { get; private set; } // Nombre del preventista / viajante

    // --- CONFIGURACIÓN COMERCIAL (ESCALABILIDAD) ---
    public bool PermiteCuentaCorriente { get; private set; } = false;
    public int DiasPlazoPago { get; private set; } = 0; // Plazo estándar (ej: 7, 15, 30 días)

    // --- AUDITORÍA Y CONCURRENCIA ---
    public bool EstaActivo { get; private set; } = true;
    public DateTime? FechaModificacion { get; private set; }
    public byte[] Version { get; private set; } = Array.Empty<byte>();

    // Constructor para ALTA de Proveedores
    public Proveedor(
        Guid id,
        string razonSocial,
        string? cuit,
        string tipoResponsabilidadIva,
        string? telefono,
        string? email,
        string? direccion,
        string? nombreContacto,
        bool permiteCuentaCorriente,
        int diasPlazoPago) : base(id)
    {
        ActualizarDatosComerciales(razonSocial, cuit, tipoResponsabilidadIva, telefono, email, direccion, nombreContacto);
        ConfigurarCreditoCompra(permiteCuentaCorriente, diasPlazoPago);
        EstaActivo = true;
    }

#pragma warning disable CS8618
    // Constructor requerido por Entity Framework Core
    protected Proveedor() { }
#pragma warning restore CS8618

    // --- MÉTODOS DE COMPORTAMIENTO SEGURO ---

    public void ActualizarDatosComerciales(string razonSocial, string? cuit, string tipoResponsabilidadIva, string? telefono, string? email, string? direccion, string? nombreContacto)
    {
        if (string.IsNullOrWhiteSpace(razonSocial))
            throw new ArgumentException("La razón social del proveedor es obligatoria.");

        RazonSocial = razonSocial.Trim();
        Cuit = string.IsNullOrWhiteSpace(cuit) ? null : cuit.Trim();
        TipoResponsabilidadIva = string.IsNullOrWhiteSpace(tipoResponsabilidadIva) ? "Responsable Inscripto" : tipoResponsabilidadIva.Trim();
        Telefono = string.IsNullOrWhiteSpace(telefono) ? null : telefono.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        Direccion = string.IsNullOrWhiteSpace(direccion) ? null : direccion.Trim();
        NombreContacto = string.IsNullOrWhiteSpace(nombreContacto) ? null : nombreContacto.Trim();

        FechaModificacion = DateTime.UtcNow;
    }

    public void ConfigurarCreditoCompra(bool permiteCtaCte, int diasPlazo)
    {
        if (diasPlazo < 0)
            throw new ArgumentException("Los días de plazo de pago no pueden ser negativos.");

        PermiteCuentaCorriente = permiteCtaCte;
        DiasPlazoPago = permiteCtaCte ? diasPlazo : 0;

        FechaModificacion = DateTime.UtcNow;
    }

    public void Desactivar()
    {
        EstaActivo = false;
        FechaModificacion = DateTime.UtcNow;
    }

    public void Activar()
    {
        EstaActivo = true;
        FechaModificacion = DateTime.UtcNow;
    }
}