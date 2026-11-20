namespace PosVentas.Infrastructure.Afip;

/// <summary>Endpoints de ARCA. Los valores por defecto son los publicados para WSAA y WSFEv1.</summary>
public sealed class AfipOptions
{
    public const string SectionName = "Afip";

    public string WsaaHomologacion { get; set; } = "https://wsaahomo.afip.gov.ar/ws/services/LoginCms";
    public string WsaaProduccion { get; set; } = "https://wsaa.afip.gov.ar/ws/services/LoginCms";
    public string WsfeHomologacion { get; set; } = "https://wswhomo.afip.gov.ar/wsfev1/service.asmx";
    public string WsfeProduccion { get; set; } = "https://servicios1.afip.gov.ar/wsfev1/service.asmx";

    public int TimeoutSegundos { get; set; } = 30;

    public string Wsaa(bool produccion) => produccion ? WsaaProduccion : WsaaHomologacion;
    public string Wsfe(bool produccion) => produccion ? WsfeProduccion : WsfeHomologacion;
}
