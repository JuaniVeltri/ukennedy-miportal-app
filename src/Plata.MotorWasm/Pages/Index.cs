using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;
using System;
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
            var xap = ParametroXap(href);
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
                var tipoEntrada = new CargadorDeApp().CargarTipoDeEntrada(pkg);
                await Avisar("plata:cargado " + tipoEntrada.FullName);
                await Runner.RunApplicationAsync(() =>
                {
                    AvisarInterop("plata:instanciando-en-runner");
                    var app = (Application)Activator.CreateInstance(tipoEntrada)!;
                    AvisarInterop("plata:instanciado rootvisual=" +
                        (app.RootVisual?.GetType().FullName ?? "null"));
                    return app;
                });
            }
            catch (Exception ex)
            {
                var raiz = ex;
                while (raiz.InnerException is not null) raiz = raiz.InnerException;
                await Avisar("plata:error " + raiz.GetType().Name + ": " + raiz.Message);
            }
        }

        private async Task Avisar(string m)
            => await JS.InvokeVoidAsync("window.external.sendMessage", m);

        // Reporte síncrono desde dentro del arranque de OpenSilver (donde su interop ya existe).
        private static void AvisarInterop(string m)
            => OpenSilver.Interop.ExecuteJavaScriptVoid(
                "window.external.sendMessage(" + System.Text.Json.JsonSerializer.Serialize(m) + ")");

        private static string? ParametroXap(string href)
        {
            var q = new Uri(href).Query.TrimStart('?');
            foreach (var par in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = par.Split('=', 2);
                if (kv.Length == 2 && kv[0] == "xap") return Uri.UnescapeDataString(kv[1]);
            }
            return null;
        }
    }
}
