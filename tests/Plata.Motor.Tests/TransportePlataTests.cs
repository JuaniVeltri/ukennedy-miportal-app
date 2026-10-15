// SPDX-License-Identifier: MIT
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using Plata.Compat;
using Xunit;

[Collection("Portero")] // Portero.Enviar es global
public class TransportePlataTests
{
    [ServiceContract]
    public interface IEco
    {
        [OperationContract(AsyncPattern = true, Action = "http://tempuri.org/IEco/Eco", ReplyAction = "http://tempuri.org/IEco/EcoResponse")]
        IAsyncResult BeginEco(string texto, AsyncCallback callback, object estado);
        string EndEco(IAsyncResult resultado);
    }

    [Fact]
    public async Task NuevaFabrica_conPortero_mandaElSobreSoapPorElPortero()
    {
        var pedidos = new List<PedidoHttp>();
        Portero.Enviar = async p =>
        {
            pedidos.Add(p);
            var sobre = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body>" +
                        "<EcoResponse xmlns=\"http://tempuri.org/\"><EcoResult>hola</EcoResult></EcoResponse></s:Body></s:Envelope>";
            await Task.Yield();
            return new RespuestaHttp(200, new[] { ("Content-Type", "text/xml; charset=utf-8") }, Encoding.UTF8.GetBytes(sobre));
        };
        try
        {
            var fabrica = Desvios.NuevaFabrica<IEco>(new BasicHttpBinding(), new EndpointAddress("http://servicios1.ejemplo.edu.ar/Eco.svc"));
            var canal = Desvios.CrearCanal(fabrica);

            var listo = new TaskCompletionSource<string>();
            canal.BeginEco("hola", r =>
            {
                try { listo.SetResult(canal.EndEco(r)); } catch (Exception e) { listo.SetException(e); }
            }, null!);

            Assert.Equal("hola", await listo.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            var pedido = Assert.Single(pedidos);
            Assert.Equal("POST", pedido.Metodo);
            Assert.Equal("http://servicios1.ejemplo.edu.ar/Eco.svc", pedido.Url);
            Assert.Contains(pedido.Cabeceras, c => c.Nombre == "SOAPAction" && c.Valor == "\"http://tempuri.org/IEco/Eco\"");
            Assert.Contains(pedido.Cabeceras, c => c.Nombre == "Content-Type" && c.Valor.StartsWith("text/xml"));
            Assert.Contains("<texto>hola</texto>", Encoding.UTF8.GetString(pedido.Cuerpo));
            // Sin WS-Addressing la acción va solo en SOAPAction, no como encabezado SOAP.
            Assert.DoesNotContain("addressing/none", Encoding.UTF8.GetString(pedido.Cuerpo));
        }
        finally { Portero.Enviar = null; }
    }

    [Fact]
    public async Task NuevaFabrica_errorHttp_llegaComoExcepcionDeComunicacion()
    {
        Portero.Enviar = _ => Task.FromResult(new RespuestaHttp(404, Array.Empty<(string, string)>(), Array.Empty<byte>()));
        try
        {
            var fabrica = Desvios.NuevaFabrica<IEco>(new BasicHttpBinding(), new EndpointAddress("http://servicios1.ejemplo.edu.ar/Eco.svc"));
            var canal = Desvios.CrearCanal(fabrica);

            var listo = new TaskCompletionSource<Exception?>();
            canal.BeginEco("x", r =>
            {
                try { canal.EndEco(r); listo.SetResult(null); } catch (Exception e) { listo.SetResult(e); }
            }, null!);

            Assert.IsAssignableFrom<CommunicationException>(await listo.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally { Portero.Enviar = null; }
    }

    [Fact]
    public void NuevaFabrica_sinPortero_dejaLaBindingOriginal()
    {
        Portero.Enviar = null;
        var fabrica = Desvios.NuevaFabrica<IEco>(new BasicHttpBinding(), new EndpointAddress("http://x/"));
        Assert.IsType<BasicHttpBinding>(fabrica.Endpoint.Binding);
    }
}
