using System;
using PosVentas.Domain.Common;

namespace PosVentas.Domain.Entities;

public class Cliente : Entity<Guid>
{
    // --- DATOS DE IDENTIFICACIÓN Y FISCALES ---
    public string RazonSocial { get; private set; } = string.Empty; // Nombre o Nombre del Comercio
    public string? Documento { get; private set; } // DNI, CUIT o CUIL
    public string TipoResponsabilidadIva { get; private set; } = "Consumidor Final"; // Responsable Inscripto, Monotributo, etc.

    // --- DATOS DE CONTACTO ---
    public string? Telefono { get; private set; }
    public string? Email { get; private set; }
    public string? Direccion { get; private set; }

    // --- CRÉDITO Y CUENTA CORRIENTE (PENSADO EN ESCALAMIENTO) ---
    public bool PermiteCuentaCorriente { get; private set; } = false;
    public decimal LimiteCredito { get; private set; } = 0.00m;

    // --- CAMPOS DE ESCALAMIENTO, AUDITORÍA Y CONCURRENCIA ---
    public bool EstaActivo { get; private set; } = true;

    // NOTA DE ARQUITECTURA: Se eliminó la propiedad FechaCreacion de aquí 
    // porque ya está heredada nativamente desde Entity<Guid>.

    public DateTime? FechaModificacion { get; private set; }
    public byte[] Version { get; private set; } = Array.Empty<byte>();

    // Constructor para ALTA de Clientes
    public Cliente(
        Guid id,
        string razonSocial,
        string? documento,
        string tipoResponsabilidadIva,
        string? telefono,
        string? email,
        string? direccion,
        bool permiteCuentaCorriente,
        decimal limiteCredito) : base(id)
    {
        ActualizarDatosPersonales(razonSocial, documento, tipoResponsabilidadIva, telefono, email, direccion);
        ConfigurarCredito(permiteCuentaCorriente, limiteCredito);
        EstaActivo = true;
    }

#pragma warning disable CS8618
    // Constructor requerido por Entity Framework Core
    protected Cliente() { }
#pragma warning restore CS8618

    // --- MÉTODOS DE COMPORTAMIENTO (COMPORTAMIENTO SEGURO DE DOMINIO) ---

    public void ActualizarDatosPersonales(string razonSocial, string? documento, string tipoResponsabilidadIva, string? telefono, string? email, string? direccion)
    {
        if (string.IsNullOrWhiteSpace(razonSocial))
            throw new ArgumentException("El nombre o razón social del cliente es obligatorio.");

        RazonSocial = razonSocial.Trim();
        Documento = string.IsNullOrWhiteSpace(documento) ? null : documento.Trim();
        TipoResponsabilidadIva = string.IsNullOrWhiteSpace(tipoResponsabilidadIva) ? "Consumidor Final" : tipoResponsabilidadIva.Trim();
        Telefono = string.IsNullOrWhiteSpace(telefono) ? null : telefono.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        Direccion = string.IsNullOrWhiteSpace(direccion) ? null : direccion.Trim();

        FechaModificacion = DateTime.UtcNow;
    }

    public void ConfigurarCredito(bool permiteCtaCte, decimal limite)
    {
        if (limite < 0)
            throw new ArgumentException("El límite de crédito no puede ser un valor negativo.");

        PermiteCuentaCorriente = permiteCtaCte;
        LimiteCredito = permiteCtaCte ? limite : 0.00m; // Si no permite Cta Cte, el límite se fuerza a 0

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