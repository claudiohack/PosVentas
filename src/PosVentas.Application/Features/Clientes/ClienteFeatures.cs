using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Enums;

namespace PosVentas.Application.Features.Clientes;

internal static class ReglasCliente
{
    public const decimal LimiteCreditoMaximo = 1_000_000_000m;
}

// ---------- Listado ----------

public sealed record ObtenerClientesQuery(string? Buscar, bool SoloCuentaCorriente = false) : IRequest<IReadOnlyList<ClienteDto>>;

public sealed class ObtenerClientesQueryValidator : AbstractValidator<ObtenerClientesQuery>
{
    public ObtenerClientesQueryValidator()
    {
        RuleFor(x => x.Buscar).MaximumLength(Cliente.LongitudMaximaRazonSocial);
    }
}

public sealed class ObtenerClientesQueryHandler(IApplicationDbContext db) : IRequestHandler<ObtenerClientesQuery, IReadOnlyList<ClienteDto>>
{
    private const int LimiteResultados = 500;

    public async Task<IReadOnlyList<ClienteDto>> Handle(ObtenerClientesQuery request, CancellationToken cancellationToken)
    {
        var query = db.Clientes.AsNoTracking().Where(c => c.EstaActivo);

        if (request.SoloCuentaCorriente)
            query = query.Where(c => c.PermiteCuentaCorriente);

        if (!string.IsNullOrWhiteSpace(request.Buscar))
        {
            var termino = request.Buscar.Trim();
            query = query.Where(c => c.RazonSocial.Contains(termino) || (c.Documento != null && c.Documento.Contains(termino)));
        }

        var clientes = await query
            .OrderByDescending(c => c.EsConsumidorFinalGenerico)
            .ThenBy(c => c.RazonSocial)
            .Take(LimiteResultados)
            .ToListAsync(cancellationToken);

        return clientes.Select(ClienteMapper.ToDto).ToList();
    }
}

// ---------- Alta ----------

public sealed record CrearClienteCommand(
    string RazonSocial,
    string? Documento,
    CondicionIva CondicionIva,
    string? Telefono,
    string? Email,
    string? Direccion,
    bool PermiteCuentaCorriente,
    decimal LimiteCredito) : IRequest<ClienteDto>;

public sealed class CrearClienteCommandValidator : AbstractValidator<CrearClienteCommand>
{
    public CrearClienteCommandValidator()
    {
        RuleFor(x => x.RazonSocial).NotEmpty().MaximumLength(Cliente.LongitudMaximaRazonSocial);
        RuleFor(x => x.Documento).MaximumLength(Cliente.LongitudMaximaDocumento)
            .Matches(@"^[0-9-]+$").When(x => !string.IsNullOrWhiteSpace(x.Documento))
            .WithMessage("El documento solo admite números y guiones.");
        RuleFor(x => x.CondicionIva).IsInEnum();
        RuleFor(x => x.Telefono).MaximumLength(30);
        RuleFor(x => x.Email).MaximumLength(150).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Direccion).MaximumLength(200);
        RuleFor(x => x.LimiteCredito).GreaterThanOrEqualTo(0).LessThanOrEqualTo(ReglasCliente.LimiteCreditoMaximo).PrecisionScale(18, 2, true);
        RuleFor(x => x.LimiteCredito).GreaterThan(0).When(x => x.PermiteCuentaCorriente)
            .WithMessage("Para habilitar la cuenta corriente hay que fijar un límite de crédito mayor a cero.");
    }
}

public sealed class CrearClienteCommandHandler(IApplicationDbContext db) : IRequestHandler<CrearClienteCommand, ClienteDto>
{
    public async Task<ClienteDto> Handle(CrearClienteCommand request, CancellationToken cancellationToken)
    {
        var documento = string.IsNullOrWhiteSpace(request.Documento) ? null : request.Documento.Trim();
        if (documento is not null && await db.Clientes.AnyAsync(c => c.Documento == documento, cancellationToken))
            throw new ConflictException($"Ya existe un cliente con el documento '{documento}'.");

        var cliente = Cliente.Crear(request.RazonSocial, documento, request.CondicionIva, request.Telefono,
            request.Email, request.Direccion, request.PermiteCuentaCorriente, request.LimiteCredito);

        db.Clientes.Add(cliente);
        await db.SaveChangesAsync(cancellationToken);
        return ClienteMapper.ToDto(cliente);
    }
}

// ---------- Configuración de crédito ----------

public sealed record ActualizarCreditoClienteCommand(Guid ClienteId, bool PermiteCuentaCorriente, decimal LimiteCredito) : IRequest<ClienteDto>;

public sealed class ActualizarCreditoClienteCommandValidator : AbstractValidator<ActualizarCreditoClienteCommand>
{
    public ActualizarCreditoClienteCommandValidator()
    {
        RuleFor(x => x.ClienteId).NotEmpty();
        RuleFor(x => x.LimiteCredito).GreaterThanOrEqualTo(0).LessThanOrEqualTo(ReglasCliente.LimiteCreditoMaximo).PrecisionScale(18, 2, true);
    }
}

public sealed class ActualizarCreditoClienteCommandHandler(IApplicationDbContext db)
    : IRequestHandler<ActualizarCreditoClienteCommand, ClienteDto>
{
    public async Task<ClienteDto> Handle(ActualizarCreditoClienteCommand request, CancellationToken cancellationToken)
    {
        var cliente = await db.Clientes.SingleOrDefaultAsync(c => c.Id == request.ClienteId, cancellationToken)
            ?? throw new NotFoundException(nameof(Cliente), request.ClienteId);

        cliente.ConfigurarCredito(request.PermiteCuentaCorriente, request.LimiteCredito);
        await db.SaveChangesAsync(cancellationToken);
        return ClienteMapper.ToDto(cliente);
    }
}

// ---------- Cuenta corriente (saldo + historial) ----------

public sealed record ObtenerCuentaCorrienteQuery(Guid ClienteId) : IRequest<CuentaCorrienteDto>;

public sealed class ObtenerCuentaCorrienteQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ObtenerCuentaCorrienteQuery, CuentaCorrienteDto>
{
    private const int LimiteMovimientos = 200;

    public async Task<CuentaCorrienteDto> Handle(ObtenerCuentaCorrienteQuery request, CancellationToken cancellationToken)
    {
        var cliente = await db.Clientes.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == request.ClienteId, cancellationToken)
            ?? throw new NotFoundException(nameof(Cliente), request.ClienteId);

        var movimientos = await db.MovimientosCuentaCorriente.AsNoTracking()
            .Where(m => m.ClienteId == cliente.Id)
            .OrderByDescending(m => m.FechaUtc)
            .Take(LimiteMovimientos)
            .Select(m => new MovimientoCuentaCorrienteDto(
                m.Id, m.FechaUtc, m.Tipo, m.Monto, m.SaldoResultante, m.VentaId, m.ReciboCobranzaId, m.Descripcion))
            .ToListAsync(cancellationToken);

        return new CuentaCorrienteDto(ClienteMapper.ToDto(cliente), movimientos);
    }
}

// ---------- Recibo de cobranza ----------

public sealed record RegistrarCobranzaCommand(
    Guid ClienteId,
    decimal Monto,
    MedioPago Medio,
    Guid CajaSesionId,
    string? Observaciones) : IRequest<ReciboCobranzaDto>;

public sealed class RegistrarCobranzaCommandValidator : AbstractValidator<RegistrarCobranzaCommand>
{
    public RegistrarCobranzaCommandValidator()
    {
        RuleFor(x => x.ClienteId).NotEmpty();
        RuleFor(x => x.CajaSesionId).NotEmpty();
        RuleFor(x => x.Monto).GreaterThan(0).LessThanOrEqualTo(ReglasCliente.LimiteCreditoMaximo).PrecisionScale(18, 2, true);
        RuleFor(x => x.Medio).Must(m => ReciboCobranza.MediosPermitidos.Contains(m))
            .WithMessage("Las cobranzas solo se aceptan en efectivo, transferencia o QR.");
        RuleFor(x => x.Observaciones).MaximumLength(ReciboCobranza.LongitudMaximaObservaciones);
    }
}

public sealed class RegistrarCobranzaCommandHandler(IApplicationDbContext db, ICurrentUserService usuarioActual)
    : IRequestHandler<RegistrarCobranzaCommand, ReciboCobranzaDto>
{
    public async Task<ReciboCobranzaDto> Handle(RegistrarCobranzaCommand request, CancellationToken cancellationToken)
    {
        var usuarioId = await Cajas.UsuarioOperador.ObtenerActivoAsync(db, usuarioActual, cancellationToken);

        // Cliente y caja de otro comercio no son visibles por el filtro global → 404
        var cliente = await db.Clientes.SingleOrDefaultAsync(c => c.Id == request.ClienteId, cancellationToken)
            ?? throw new NotFoundException(nameof(Cliente), request.ClienteId);
        var sesion = await db.CajaSesiones.SingleOrDefaultAsync(s => s.Id == request.CajaSesionId, cancellationToken)
            ?? throw new NotFoundException("Sesión de caja", request.CajaSesionId);

        var (recibo, abono, ingresoCaja) = ReciboCobranza.Registrar(
            cliente, sesion, usuarioId, request.Medio, request.Monto, request.Observaciones);

        db.RecibosCobranza.Add(recibo);
        db.MovimientosCuentaCorriente.Add(abono);
        db.MovimientosCaja.Add(ingresoCaja);

        // Una transacción: recibo, saldo del cliente, asiento de cuenta, ingreso y totales del turno.
        await db.SaveChangesAsync(cancellationToken);

        return new ReciboCobranzaDto(recibo.Id, cliente.Id, cliente.RazonSocial, sesion.Id, recibo.Medio,
            recibo.Monto, recibo.SaldoAnterior, recibo.SaldoResultante, recibo.FechaUtc);
    }
}
