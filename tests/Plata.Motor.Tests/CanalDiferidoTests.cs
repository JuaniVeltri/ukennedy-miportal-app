// SPDX-License-Identifier: MIT
using System.Net;
using System.ServiceModel;
using System.Text;
using Plata.Compat;
using Xunit;

[Collection("Portero")] // usa WCF real: no debe correr mientras otra prueba cambia Portero.Enviar
public class CanalDiferidoTests
{
    // Contrato al estilo Silverlight: solo el par asincrónico Begin/End.
    [ServiceContract]
    public interface IEco
    {
        [OperationContract(AsyncPattern = true, Action = "http://tempuri.org/IEco/Eco", ReplyAction = "http://tempuri.org/IEco/EcoResponse")]
        IAsyncResult BeginEco(string texto, AsyncCallback callback, object estado);
        string EndEco(IAsyncResult resultado);
    }

    [Fact]
    public async Task BeginEnd_abreLaFabricaSinBloquearYDevuelveLaRespuesta()
    {
        using var servidor = ServidorSoap("<EcoResponse xmlns=\"http://tempuri.org/\"><EcoResult>hola</EcoResult></EcoResponse>", out var url);
        var fabrica = new ChannelFactory<IEco>(new BasicHttpBinding(), new EndpointAddress(url));

        var canal = Desvios.CrearCanal(fabrica);
        Assert.Equal(CommunicationState.Created, fabrica.State); // no abrió en forma sincrónica

        var listo = new TaskCompletionSource<string>();
        var estado = new object();
        IAsyncResult? real = null;
        var ar = canal.BeginEco("hola", r =>
        {
            real = (IAsyncResult?)r.GetType().GetProperty("Real")?.GetValue(r);
            try { listo.SetResult(canal.EndEco(r)); } catch (Exception e) { listo.SetException(e); }
        }, estado);

        Assert.Same(estado, ar.AsyncState);
        Assert.Equal("hola", await listo.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        // La llamada real recibe el estado original (hay canales que lo leen).
        Assert.Same(estado, real?.AsyncState);
        Desvios.Cerrar((ICommunicationObject)canal);
        Desvios.CerrarObjeto(fabrica);
    }

    [Fact]
    public async Task BeginEnd_errorDelServicio_seLanzaEnEnd()
    {
        var fabrica = new ChannelFactory<IEco>(new BasicHttpBinding(), new EndpointAddress("http://127.0.0.1:9/nadie"));
        var canal = Desvios.CrearCanal(fabrica);

        var listo = new TaskCompletionSource<Exception?>();
        canal.BeginEco("x", r =>
        {
            try { canal.EndEco(r); listo.SetResult(null); } catch (Exception e) { listo.SetResult(e); }
        }, null!);

        var error = await listo.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.IsAssignableFrom<CommunicationException>(error);
    }

    // Un servidor HTTP mínimo que responde siempre el mismo cuerpo SOAP.
    private static HttpListener ServidorSoap(string cuerpo, out string url)
    {
        var puerto = new Random().Next(20000, 40000);
        url = $"http://127.0.0.1:{puerto}/eco/";
        var l = new HttpListener();
        l.Prefixes.Add(url);
        l.Start();
        _ = Task.Run(async () =>
        {
            while (l.IsListening)
            {
                HttpListenerContext c;
                try { c = await l.GetContextAsync(); } catch { return; }
                var sobre = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body>" + cuerpo + "</s:Body></s:Envelope>";
                var b = Encoding.UTF8.GetBytes(sobre);
                c.Response.ContentType = "text/xml; charset=utf-8";
                await c.Response.OutputStream.WriteAsync(b);
                c.Response.Close();
            }
        });
        return l;
    }
}
