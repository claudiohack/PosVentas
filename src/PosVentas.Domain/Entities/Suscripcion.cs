using PosVentas.Domain.Common;
using PosVentas.Domain.Enums;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Suscripción de un comercio a un plan. El estado no se guarda: se deriva de <see cref="VenceUtc"/> y del
/// momento de la consulta (sin procesos programados que lo "pasen" de estado):
/// en prueba / activa hasta el vencimiento → en gracia <see cref="DiasGracia"/> días → solo lectura.
/// No es una entidad de tenant: la administran los administradores de la plataforma (HS Tech).
/// </summary>
public sealed class Suscripcion : Entity<Guid>
{
    public const int DiasPrueba = 30;
    public const int DiasGracia = 7;
    public const int MesesMaximosPorPago = 24;

    public Guid ComercioId { get; private set; }
    public Guid PlanId { get; private set; }
    public Plan? Plan { get; private set; }

    /// <summary>true hasta el primer pago: el período vigente es la prueba gratuita.</summary>
    public bool EnPrueba { get; private set; }

    /// <summary>Fin del período pagado (o de la prueba). Después corren los días de gracia.</summary>
    public DateTime VenceUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public DateTime FinGraciaUtc => VenceUtc.AddDays(DiasGracia);

    private Suscripcion(Guid id) : base(id) { }

    // Requerido por EF Core
    private Suscripcion() { }

    /// <summary>Suscripción de un comercio nuevo: <see cref="DiasPrueba"/> días gratis con el plan indicado.</summary>
    public static Suscripcion IniciarPrueba(Guid comercioId, Plan plan, DateTime ahoraUtc)
    {
        if (comercioId == Guid.Empty)
            throw new DomainException("El comercio de la suscripción es obligatorio.");
        if (!plan.EstaActivo)
            throw new DomainException($"El plan '{plan.Nombre}' no está disponible.");

        return new Suscripcion(Guid.NewGuid())
        {
            ComercioId = comercioId,
            PlanId = plan.Id,
            Plan = plan,
            EnPrueba = true,
            VenceUtc = ahoraUtc.AddDays(DiasPrueba)
        };
    }

    public EstadoSuscripcion EstadoEn(DateTime ahoraUtc) =>
        ahoraUtc <= VenceUtc ? (EnPrueba ? EstadoSuscripcion.Prueba : EstadoSuscripcion.Activa)
        : ahoraUtc <= FinGraciaUtc ? EstadoSuscripcion.EnGracia
        : EstadoSuscripcion.SoloLectura;

    /// <summary>Días que faltan para el próximo hito: el vencimiento o, en gracia, el pase a solo lectura (0 si ya pasó).</summary>
    public int DiasRestantesEn(DateTime ahoraUtc)
    {
        var hito = ahoraUtc <= VenceUtc ? VenceUtc : FinGraciaUtc;
        return Math.Max(0, (int)Math.Ceiling((hito - ahoraUtc).TotalDays));
    }

    /// <summary>
    /// Acredita <paramref name="meses"/> meses de servicio. Si la suscripción está al día, se suman al vencimiento
    /// (los días de prueba que quedaban se respetan); si ya venció, el nuevo período corre desde hoy.
    /// </summary>
    public DateTime AcreditarMeses(int meses, DateTime ahoraUtc)
    {
        if (meses is < 1 or > MesesMaximosPorPago)
            throw new DomainException($"Se pueden acreditar entre 1 y {MesesMaximosPorPago} meses por pago.");

        var desde = VenceUtc > ahoraUtc ? VenceUtc : ahoraUtc;
        VenceUtc = desde.AddMonths(meses);
        EnPrueba = false;
        MarcarModificado();
        return VenceUtc;
    }

    public void CambiarPlan(Plan plan)
    {
        if (plan.Id == PlanId)
            return;
        if (!plan.EstaActivo)
            throw new DomainException($"El plan '{plan.Nombre}' no está disponible.");
        PlanId = plan.Id;
        Plan = plan;
        MarcarModificado();
    }
}

/// <summary>
/// Pago acreditado a una suscripción (historial). En este paso los registra HS Tech a mano; con la integración
/// de Mercado Pago llegarán también por webhook, con la referencia del pago.
/// </summary>
public sealed class PagoSuscripcion : Entity<Guid>
{
    public const int LongitudMaximaReferencia = 100;
    public const int LongitudMaximaNota = 250;

    public Guid SuscripcionId { get; private set; }
    public Guid ComercioId { get; private set; }
    public Guid PlanId { get; private set; }
    public decimal Monto { get; private set; }
    public int Meses { get; private set; }

    /// <summary>Vencimiento de la suscripción después de acreditar este pago.</summary>
    public DateTime PeriodoHastaUtc { get; private set; }

    public OrigenPagoSuscripcion Origen { get; private set; }

    /// <summary>Identificador externo (p. ej. el id del pago en Mercado Pago). Único por origen.</summary>
    public string? Referencia { get; private set; }

    public string? Nota { get; private set; }
    public Guid? RegistradoPorUsuarioId { get; private set; }

    private PagoSuscripcion(Guid id) : base(id) { }

    // Requerido por EF Core
    private PagoSuscripcion() { }

    /// <summary>Acredita el pago en la suscripción y devuelve el registro para el historial.</summary>
    public static PagoSuscripcion Acreditar(Suscripcion suscripcion, decimal monto, int meses, OrigenPagoSuscripcion origen,
        string? referencia, string? nota, Guid? registradoPorUsuarioId, DateTime ahoraUtc)
    {
        if (monto is < 0 or > Plan.PrecioMaximo * Suscripcion.MesesMaximosPorPago || decimal.Round(monto, 2) != monto)
            throw new DomainException("El monto del pago no es válido.");

        var hasta = suscripcion.AcreditarMeses(meses, ahoraUtc);
        return new PagoSuscripcion(Guid.NewGuid())
        {
            SuscripcionId = suscripcion.Id,
            ComercioId = suscripcion.ComercioId,
            PlanId = suscripcion.PlanId,
            Monto = monto,
            Meses = meses,
            PeriodoHastaUtc = hasta,
            Origen = origen,
            Referencia = string.IsNullOrWhiteSpace(referencia) ? null : referencia.Trim(),
            Nota = string.IsNullOrWhiteSpace(nota) ? null : nota.Trim(),
            RegistradoPorUsuarioId = registradoPorUsuarioId
        };
    }
}
