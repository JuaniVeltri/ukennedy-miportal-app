using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using OpenSilver.WebAssembly;
using Plata.Motor.Puente;
using Plata.Motor.Xap;

namespace Plata.MotorWasm.Pages
{
    [Route("/")]
    public class Index : ComponentBase
    {
        [Inject] public IJSRuntime JS { get; set; } = default!;

        protected override void BuildRenderTree(RenderTreeBuilder __builder)
        {
        }

        protected async override Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();
            await Avisar("plata:init");

            var href = await JS.InvokeAsync<string>("eval", "window.location.href");
            var xap = Parametro(href, "xap");
            await Avisar("plata:xap=" + (xap ?? "null"));

            if (xap is null)
            {
                await Runner.RunApplicationAsync<Plata.MotorApp.App>();
                return;
            }

            try
            {
                using var http = new HttpClient();
                var absoluta = new Uri(new Uri(href), xap); // plata://app/ + ruta relativa
                var bytes = await http.GetByteArrayAsync(absoluta);
                await Avisar("plata:bajado " + bytes.Length + "B");
                var pkg = XapReader.Leer(new System.IO.MemoryStream(bytes));
                Plata.Compat.Avisos.Emitido += m => AvisarInterop("plata:aviso " + m);
                await CanalDeRed.ConectarAsync(JS);
                // Modo diagnóstico (lo pide el host): excepciones con su stack y estado periódico.
                var diagnostico = (await http.GetStringAsync(new Uri(new Uri(href), "/_plata/diagnostico"))).Trim() == "1";
                if (diagnostico) Diagnostico.InformarExcepciones(AvisarInterop);
                // De dónde vino el .xap: Silverlight resuelve contra él las direcciones relativas
                // (Host.Source, WebClient). El host da la carpeta de origen; vacío = local.
                var origen = await http.GetStringAsync(new Uri(new Uri(href), "/_plata/origen"));
                if (origen.Length > 0)
                    Plata.Compat.Origen.Xap = new Uri(new Uri(origen), System.IO.Path.GetFileName(xap));
                var pagina = await http.GetStringAsync(new Uri(new Uri(href), "/_plata/pagina"));
                if (pagina.Length > 0) Plata.Compat.Origen.Pagina = new Uri(pagina);
                // Rastreo para desarrollo: patrones "Tipo::Método" (uno por línea), si el host los da.
                var rastrear = (await http.GetStringAsync(new Uri(new Uri(href), "/_plata/rastrear")))
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (rastrear.Length > 0)
                    Plata.Motor.Puente.ReferenceRetargeter.Rastrear = m => rastrear.Any(p => m.StartsWith(p, StringComparison.Ordinal));
                var cargador = new CargadorDeApp();
                Plata.Compat.Ensamblados.Cargar = cargador.CargarEnsamblado;
                var tipoEntrada = cargador.CargarTipoDeEntrada(pkg);
                await Avisar("plata:cargado " + tipoEntrada.FullName);
                // OpenSilver lee los InitParams de un <param> dentro de su raíz al crear la App.
                // El host los sirve aparte (no por la URL); vacío = la app no tiene.
                var initParams = await http.GetStringAsync(new Uri(new Uri(href), "/_plata/init-params"));
                if (initParams.Length > 0)
                {
                    await JS.InvokeVoidAsync("eval",
                        "(function(v){var p=document.createElement('param');p.name='InitParams';p.value=v;" +
                        "document.getElementById('opensilver-root').appendChild(p);})(" +
                        System.Text.Json.JsonSerializer.Serialize(initParams) + ")");
                    await Avisar("plata:initParams " + initParams.Split(',').Length + " claves");
                }
                await Runner.RunApplicationAsync(() =>
                {
                    AvisarInterop("plata:instanciando-en-runner");
                    Diagnostico.AppCreada = true;
                    var app = (Application)Activator.CreateInstance(tipoEntrada)!;
                    app.Theme = new Plata.Compat.TemaPlata();
                    app.UnhandledException += (_, ev) =>
                    {
                        var r = ev.ExceptionObject;
                        while (r?.InnerException is not null) r = r.InnerException;
                        AvisarInterop("plata:startup-error " + r?.GetType().Name + ": " + r?.Message
                            + " @ " + string.Join(" | ", (r?.StackTrace ?? "").Split('\n').Take(8).Select(l => l.Trim())));
                    };
                    AvisarInterop("plata:instanciado rootvisual=" +
                        (app.RootVisual?.GetType().FullName ?? "null"));
                    return app;
                });
                _ = AvisarCuandoSeVea();
                if (!diagnostico) return;
                for (var i = 1; i <= 6; i++)
                {
                    await Task.Delay(5000);
                    var nodos = await JS.InvokeAsync<int>("eval", "document.querySelectorAll('#opensilver-root *').length");
                    await Avisar($"plata:estado t={i * 5}s rootvisual=" +
                        (Application.Current?.RootVisual?.GetType().FullName ?? "null") + " nodos=" + nodos);
                }
            }
            catch (Exception ex)
            {
                var raiz = ex;
                while (raiz.InnerException is not null) raiz = raiz.InnerException;
                await Avisar("plata:error " + raiz.GetType().Name + ": " + raiz.Message);
            }
        }

        // La ventana de Plata muestra "Abriendo…" hasta que la app tiene algo en pantalla.
        private async Task AvisarCuandoSeVea()
        {
            for (var i = 0; i < 480 && Application.Current?.RootVisual is null; i++) await Task.Delay(250);
            await Avisar("plata:visible");
        }

        private async Task Avisar(string m)
            => await JS.InvokeVoidAsync("window.external.sendMessage", m);

        // Reporte síncrono desde dentro del arranque de OpenSilver (donde su interop ya existe).
        private static void AvisarInterop(string m)
            => OpenSilver.Interop.ExecuteJavaScriptVoid(
                "window.external.sendMessage(" + System.Text.Json.JsonSerializer.Serialize(m) + ")");

        private static string? Parametro(string href, string nombre)
        {
            var q = new Uri(href).Query.TrimStart('?');
            foreach (var par in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = par.Split('=', 2);
                if (kv.Length == 2 && kv[0] == nombre) return Uri.UnescapeDataString(kv[1]);
            }
            return null;
        }
    }
}
