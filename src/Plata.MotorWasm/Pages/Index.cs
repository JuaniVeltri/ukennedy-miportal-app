using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
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
        protected override void BuildRenderTree(RenderTreeBuilder __builder)
        {
        }

        protected async override Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();
            Avisar("plata:init");

            // ?xap=<ruta relativa al host> ejecuta una app Silverlight; sin él, la pantalla de prueba.
            var xap = ParametroXap();
            Avisar("plata:xap=" + (xap ?? "null"));
            if (xap is null)
            {
                await Runner.RunApplicationAsync<Plata.MotorApp.App>();
                return;
            }

            try
            {
                using var http = new HttpClient();
                var bytes = await http.GetByteArrayAsync(xap);
                var pkg = XapReader.Leer(new System.IO.MemoryStream(bytes));
                var tipoEntrada = new CargadorDeApp().CargarTipoDeEntrada(pkg);
                Avisar("plata:cargado " + tipoEntrada.FullName);
                await Runner.RunApplicationAsync(() => (Application)Activator.CreateInstance(tipoEntrada)!);
                Avisar("plata:corriendo");
            }
            catch (Exception ex)
            {
                Avisar("plata:error " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void Avisar(string m)
            => OpenSilver.Interop.ExecuteJavaScriptVoid(
                "if(window.external&&window.external.sendMessage)window.external.sendMessage(" +
                System.Text.Json.JsonSerializer.Serialize(m) + ")");

        private static string? ParametroXap()
        {
            var q = new Uri(OpenSilver.Interop.ExecuteJavaScriptGetResult<string>("window.location.href")).Query;
            foreach (var par in q.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = par.Split('=', 2);
                if (kv.Length == 2 && kv[0] == "xap") return Uri.UnescapeDataString(kv[1]);
            }
            return null;
        }
    }
}
