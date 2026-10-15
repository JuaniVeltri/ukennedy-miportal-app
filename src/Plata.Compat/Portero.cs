// SPDX-License-Identifier: MIT
namespace Plata.Compat;

/// <summary>Un pedido HTTP que la app quiere hacer; lo hace el host (ver <see cref="Portero"/>).</summary>
public sealed record PedidoHttp(string Metodo, string Url, IReadOnlyList<(string Nombre, string Valor)> Cabeceras, byte[] Cuerpo);

/// <summary>La respuesta del host a un <see cref="PedidoHttp"/>.</summary>
public sealed record RespuestaHttp(int Estado, IReadOnlyList<(string Nombre, string Valor)> Cabeceras, byte[] Cuerpo);

/// <summary>
/// Salida de red de las apps. El motor corre en el navegador: no puede configurar el
/// <c>HttpClientHandler</c> como lo hace WCF, y el navegador bloquea (CORS) las llamadas a los
/// servidores de la institución. Por eso la red la hace el host (el Portero del diseño, §4.8),
/// que aplica sus propias reglas. El motor conecta acá el canal hacia el host; si no hay
/// ninguno (p. ej. en escritorio), las apps usan el transporte HTTP normal de WCF.
/// </summary>
public static class Portero
{
    public static Func<PedidoHttp, Task<RespuestaHttp>>? Enviar { get; set; }
}
