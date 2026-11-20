using System.Collections.Concurrent;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Enums;

namespace PosVentas.IntegrationTests.Infrastructure;

/// <summary>
/// Doble de ARCA para los tests: numera correlativamente por comercio / punto de venta / tipo, otorga CAE de 14 dígitos
/// y aplica los mismos controles de importes que WSFEv1. Los comportamientos especiales se programan por comercio,
/// así los tests (que comparten la instancia) no se pisan entre sí.
/// </summary>
public sealed class AfipServiceFalso : IAfipService
{
    /// <summary>CUIT con dígito verificador correcto que el padrón simulado de ARCA no reconoce (error 10013).</summary>
    public const string CuitReceptorNoRegistrado = "20123456786";

    private readonly ConcurrentDictionary<(Guid Comercio, int PuntoVenta, TipoComprobante Tipo), long> _numeros = new();
    private readonly ConcurrentDictionary<Guid, MensajeAfip> _rechazosProgramados = new();
    private readonly ConcurrentDictionary<Guid, bool> _caidasProgramadas = new();
    private readonly ConcurrentQueue<SolicitudComprobanteAfip> _solicitudes = new();

    public IReadOnlyList<SolicitudComprobanteAfip> SolicitudesDe(Guid comercioId) =>
        _solicitudes.Where(s => s.ComercioId == comercioId).ToList();

    /// <summary>La próxima solicitud de este comercio será rechazada con el código indicado.</summary>
    public void RechazarProxima(Guid comercioId, int codigo, string mensaje) =>
        _rechazosProgramados[comercioId] = new MensajeAfip(codigo, mensaje);

    /// <summary>La próxima solicitud de este comercio fallará por comunicación (timeout / red).</summary>
    public void SimularCaidaProxima(Guid comercioId) => _caidasProgramadas[comercioId] = true;

    public Task<ResultadoAutorizacionAfip> AutorizarComprobanteAsync(SolicitudComprobanteAfip s, CancellationToken ct)
    {
        _solicitudes.Enqueue(s);

        if (_caidasProgramadas.TryRemove(s.ComercioId, out _))
            throw new AfipNoDisponibleException("Timeout simulado al conectar con WSFEv1.");

        if (_rechazosProgramados.TryRemove(s.ComercioId, out var programado))
            return Task.FromResult(Rechazo(programado));

        if (s.DocTipo == 80 && s.DocNro.ToString() == CuitReceptorNoRegistrado)
            return Task.FromResult(Rechazo(new MensajeAfip(10013,
                "Si DocTipo = 80 (CUIT), el campo DocNro debe ser un CUIT registrado en el padrón de ARCA.")));

        // Controles de importes equivalentes a los de WSFEv1 (10048 / 10051)
        if (s.ImporteTotal != s.ImporteNoGravado + s.ImporteNeto + s.ImporteExento + s.ImporteIva)
            return Task.FromResult(Rechazo(new MensajeAfip(10048, "ImpTotal debe ser igual a la suma de ImpTotConc + ImpNeto + ImpOpEx + ImpTrib + ImpIVA.")));
        if (s.Iva.Sum(i => i.Importe) != s.ImporteIva || (s.Iva.Count > 0 && s.Iva.Sum(i => i.BaseImponible) != s.ImporteNeto))
            return Task.FromResult(Rechazo(new MensajeAfip(10051, "La suma de los importes del array de IVA no coincide con ImpIVA / ImpNeto.")));
        if (s.Tipo == TipoComprobante.FacturaC && (s.ImporteIva != 0 || s.Iva.Count > 0))
            return Task.FromResult(Rechazo(new MensajeAfip(10071, "Para comprobantes tipo C el objeto IVA no debe informarse.")));

        var numero = _numeros.AddOrUpdate((s.ComercioId, s.PuntoVenta, s.Tipo), 1, (_, actual) => actual + 1);
        var cae = Random.Shared.NextInt64(70_000_000_000_000, 79_999_999_999_999).ToString();

        return Task.FromResult(new ResultadoAutorizacionAfip(
            true, numero, cae, s.Fecha.AddDays(10), [], []));
    }

    public Task<EstadoServicioAfip> VerificarConexionAsync(Guid comercioId, TipoComprobante tipo, CancellationToken ct) =>
        Task.FromResult(new EstadoServicioAfip(true, true, 0, false, "Conexión simulada."));

    private static ResultadoAutorizacionAfip Rechazo(MensajeAfip error) => new(false, null, null, null, [error], []);
}
