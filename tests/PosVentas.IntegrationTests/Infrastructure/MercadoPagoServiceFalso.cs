using System.Collections.Concurrent;
using PosVentas.Application.Common.Interfaces;

namespace PosVentas.IntegrationTests.Infrastructure;

/// <summary>
/// Mercado Pago simulado en memoria: los tests crean suscripciones por la API, las autorizan o cobran con estos
/// métodos (como lo haría el pagador en Mercado Pago) y después envían el webhook firmado.
/// </summary>
public sealed class MercadoPagoServiceFalso : IMercadoPagoService
{
    public sealed record Preaprobacion(long Orden, string Id, string Referencia, string Email, decimal Monto, string Motivo, string UrlRetorno)
    {
        public string Estado { get; set; } = EstadosPreaprobacion.Pendiente;
    }

    private readonly ConcurrentDictionary<string, Preaprobacion> _preaprobaciones = new();
    private readonly ConcurrentDictionary<string, CobroMercadoPago> _cobros = new();
    private long _ultimoCobro = 9_000_000_000;
    private long _ultimaPreaprobacion;

    public bool Habilitado => true;

    /// <summary>Ids de débitos que la app pidió cancelar, en orden.</summary>
    public ConcurrentQueue<string> Canceladas { get; } = new();

    public Preaprobacion Obtener(string id) => _preaprobaciones[id];

    /// <summary>Última suscripción creada con ese email de pagador.</summary>
    public Preaprobacion UltimaDe(string email) =>
        _preaprobaciones.Values.Where(p => p.Email == email).OrderBy(p => p.Orden).Last();

    public void Autorizar(string id) => _preaprobaciones[id].Estado = EstadosPreaprobacion.Autorizada;

    public void Pausar(string id) => _preaprobaciones[id].Estado = EstadosPreaprobacion.Pausada;

    private readonly ConcurrentDictionary<string, CobroMercadoPago> _cobrosNoDisponibles = new();

    /// <summary>
    /// Como el Mercado Pago real: el cobro se avisa antes de poder consultarlo (GET responde 404 hasta
    /// <see cref="HacerDisponible"/>).
    /// </summary>
    public string CobrarConDemora(string preaprobacionId)
    {
        var id = Cobrar(preaprobacionId);
        _cobros.TryRemove(id, out var cobro);
        _cobrosNoDisponibles[id] = cobro!;
        return id;
    }

    public void HacerDisponible(string cobroId)
    {
        if (_cobrosNoDisponibles.TryRemove(cobroId, out var cobro))
            _cobros[cobroId] = cobro;
    }

    /// <summary>Simula el cobro mensual de una suscripción y devuelve su id.</summary>
    public string Cobrar(string preaprobacionId, bool aprobado = true)
    {
        var id = Interlocked.Increment(ref _ultimoCobro).ToString();
        _cobros[id] = new CobroMercadoPago(id, preaprobacionId, _preaprobaciones[preaprobacionId].Monto, aprobado,
            aprobado ? "processed/approved" : "recycling/rejected");
        return id;
    }

    public Task<PreaprobacionCreada> CrearPreaprobacionAsync(SolicitudPreaprobacion s, CancellationToken ct)
    {
        var id = $"2c938084{Guid.NewGuid():N}"[..32];
        _preaprobaciones[id] = new Preaprobacion(Interlocked.Increment(ref _ultimaPreaprobacion), id, s.ReferenciaExterna, s.EmailPagador, s.MontoMensual, s.Motivo, s.UrlRetorno);
        return Task.FromResult(new PreaprobacionCreada(id, $"https://www.mercadopago.com.ar/subscriptions/checkout?preapproval_id={id}"));
    }

    public Task<PreaprobacionMercadoPago?> ObtenerPreaprobacionAsync(string id, CancellationToken ct) =>
        Task.FromResult(_preaprobaciones.TryGetValue(id, out var p)
            ? new PreaprobacionMercadoPago(p.Id, p.Estado, p.Referencia, p.Monto)
            : null);

    public Task<CobroMercadoPago?> ObtenerCobroAsync(string id, CancellationToken ct) =>
        Task.FromResult(_cobros.TryGetValue(id, out var c) ? c : null);

    public Task CancelarPreaprobacionAsync(string id, CancellationToken ct)
    {
        if (_preaprobaciones.TryGetValue(id, out var p))
            p.Estado = EstadosPreaprobacion.Cancelada;
        Canceladas.Enqueue(id);
        return Task.CompletedTask;
    }
}
