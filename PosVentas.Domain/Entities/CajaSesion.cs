using System;
using PosVentas.Domain.Common;

namespace PosVentas.Domain.Entities;

public class CajaSesion : Entity<Guid>
{
    // --- RELACIONES ---
    public Guid CajaId { get; private set; }
    public Caja Caja { get; private set; } = null!;

    // Guardamos el usuario de forma simple por el momento (escalable a tu entidad de Usuario futuro)
    public string UsuarioApertura { get; private set; } = string.Empty;
    public string? UsuarioCierre { get; private set; }

    // --- TIEMPOS ---
    public DateTime FechaApertura { get; private set; }
    public DateTime? FechaCierre { get; private set; }

    // --- VALORES ECONÓMICOS ---
    public decimal MontoApertura { get; private set; } // El "fondo de caja" inicial
    public decimal? MontoCierreSistema { get; private set; } // Lo que el sistema calculó matemáticamente
    public decimal? MontoCierreReal { get; private set; }    // Lo que el cajero contó físicamente en billetes
    public decimal? DiferenciaArqueo { get; private set; }   // Sobrante o Faltante calculado al cerrar

    // --- ESTADO DEL TURNO ---
    public bool EnCurso { get; private set; } = true;
    public string? Observaciones { get; private set; }

    // Constructor para APERTURA de Caja (Turno nuevo)
    public CajaSesion(Guid id, Guid cajaId, string usuarioApertura, decimal montoApertura) : base(id)
    {
        if (montoApertura < 0)
            throw new ArgumentException("El monto de apertura no puede ser un valor negativo.");

        if (string.IsNullOrWhiteSpace(usuarioApertura))
            throw new ArgumentException("El usuario que realiza la apertura es obligatorio.");

        CajaId = cajaId;
        UsuarioApertura = usuarioApertura.Trim();
        MontoApertura = montoApertura;
        FechaApertura = DateTime.UtcNow;
        EnCurso = true;
    }

#pragma warning disable CS8618
    // Constructor requerido por Entity Framework Core
    protected CajaSesion() { }
#pragma warning restore CS8618

    // --- MÉTODOS DE NEGOCIO (CIERRE Y ARQUEO) ---

    public void CerrarSesion(string usuarioCierre, decimal montoCierreSistema, decimal montoCierreReal, string? observaciones)
    {
        if (!EnCurso)
            throw new InvalidOperationException("Esta sesión de caja ya se encuentra cerrada.");

        if (string.IsNullOrWhiteSpace(usuarioCierre))
            throw new ArgumentException("El usuario que realiza el cierre es obligatorio.");

        if (montoCierreReal < 0)
            throw new ArgumentException("El monto real declarado en el cierre no puede ser negativo.");

        UsuarioCierre = usuarioCierre.Trim();
        FechaCierre = DateTime.UtcNow;
        MontoCierreSistema = montoCierreSistema;
        MontoCierreReal = montoCierreReal;

        // Calculamos la diferencia de arqueo de forma nativa en el dominio
        // Positivo = Sobrante, Negativo = Faltante, 0 = Caja Perfecta
        DiferenciaArqueo = montoCierreReal - montoCierreSistema;

        Observaciones = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim();
        EnCurso = false;
    }
}