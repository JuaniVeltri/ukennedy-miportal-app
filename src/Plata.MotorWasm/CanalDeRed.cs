// SPDX-License-Identifier: MIT
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Plata.Compat;

namespace Plata.MotorWasm
{
    /// <summary>
    /// Une el <see cref="Portero"/> de las apps con el host: cada pedido HTTP sale como mensaje
    /// "plata-net:{json}" por el canal de Photino, y la respuesta vuelve por el mismo canal.
    /// </summary>
    public static class CanalDeRed
    {
        private const string Prefijo = "plata-net:";
        private static readonly ConcurrentDictionary<int, TaskCompletionSource<RespuestaHttp>> Pendientes = new();
        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        private static int _ultimo;
        private static IJSRuntime _js = default!;

        public static async Task ConectarAsync(IJSRuntime js)
        {
            _js = js;
            await js.InvokeVoidAsync("eval",
                "window.external.receiveMessage(function (m) {" +
                "  if (m.startsWith('" + Prefijo + "'))" +
                "    DotNet.invokeMethodAsync('Plata.MotorWasm', 'PlataRespuestaDeRed', m.substring(" + Prefijo.Length + "));" +
                "});");
            Portero.Enviar = EnviarAsync;
            Ventanas.Abrir = u => _ = js.InvokeVoidAsync("window.external.sendMessage", "plata-abrir:" + u.AbsoluteUri).AsTask();
        }

        private static async Task<RespuestaHttp> EnviarAsync(PedidoHttp p)
        {
            var id = Interlocked.Increment(ref _ultimo);
            var espera = new TaskCompletionSource<RespuestaHttp>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pendientes[id] = espera;
            lock (Orden) Orden.AddLast(id);
            var json = JsonSerializer.Serialize(new
            {
                id,
                metodo = p.Metodo,
                url = p.Url,
                cabeceras = p.Cabeceras.Select(c => new[] { c.Nombre, c.Valor }),
                cuerpo = Convert.ToBase64String(p.Cuerpo),
            }, Json);
            await _js.InvokeVoidAsync("window.external.sendMessage", Prefijo + json);
            return await espera.Task;
        }

        // Las respuestas se entregan en el orden en que se hicieron los pedidos. Las apps de
        // Silverlight corrían sobre el navegador de su época (pocas conexiones por servidor, en
        // cola) y algunas dependen de ese orden: p. ej. GGI muestra una pantalla solo si sus
        // traducciones llegan antes que sus datos.
        private static readonly LinkedList<int> Orden = new();
        private static readonly Dictionary<int, Respuesta> Llegadas = new();

        [JSInvokable("PlataRespuestaDeRed")]
        public static void RecibirRespuesta(string json)
        {
            var r = JsonSerializer.Deserialize<Respuesta>(json, Json)!;
            var listas = new List<Respuesta>();
            lock (Orden)
            {
                Llegadas[r.Id] = r;
                while (Orden.First is { } primero && Llegadas.Remove(primero.Value, out var siguiente))
                {
                    Orden.RemoveFirst();
                    listas.Add(siguiente);
                }
            }
            foreach (var lista in listas) Entregar(lista);
        }

        private static void Entregar(Respuesta r)
        {
            if (!Pendientes.TryRemove(r.Id, out var espera)) return;
            if (r.Error is not null)
            {
                espera.SetException(new InvalidOperationException("Portero: " + r.Error));
                return;
            }
            var cabeceras = (r.Cabeceras ?? Array.Empty<string[]>())
                .Where(c => c.Length == 2).Select(c => (c[0], c[1])).ToList();
            espera.SetResult(new RespuestaHttp(r.Estado, cabeceras, Convert.FromBase64String(r.Cuerpo ?? "")));
        }

        private sealed record Respuesta(int Id, int Estado, string[][]? Cabeceras, string? Cuerpo, string? Error);
    }
}
