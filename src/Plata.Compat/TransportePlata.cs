// SPDX-License-Identifier: MIT
using System.ServiceModel;
using System.ServiceModel.Channels;

namespace Plata.Compat;

/// <summary>
/// Transporte WCF que reemplaza al HTTP: arma el mensaje con el encoder de la binding
/// (SOAP de texto en basicHttpBinding) y lo manda por el <see cref="Portero"/>.
/// </summary>
public sealed class TransportePlata : TransportBindingElement
{
    private readonly string _esquema;

    public TransportePlata(string esquema, long maxRecibido)
    {
        _esquema = esquema;
        MaxReceivedMessageSize = maxRecibido;
    }

    public override string Scheme => _esquema;

    public override BindingElement Clone() => new TransportePlata(_esquema, MaxReceivedMessageSize);

    public override bool CanBuildChannelFactory<TChannel>(BindingContext context)
        => typeof(TChannel) == typeof(IRequestChannel);

    public override IChannelFactory<TChannel> BuildChannelFactory<TChannel>(BindingContext context)
    {
        if (typeof(TChannel) != typeof(IRequestChannel))
            throw new NotSupportedException($"Plata: el transporte solo admite IRequestChannel (pedido: {typeof(TChannel).Name}).");
        var codificacion = context.Binding.Elements.Find<MessageEncodingBindingElement>()
                           ?? new TextMessageEncodingBindingElement(MessageVersion.Soap11, System.Text.Encoding.UTF8);
        return (IChannelFactory<TChannel>)(object)new FabricaPlata(context.Binding, codificacion.CreateMessageEncoderFactory().Encoder);
    }

    private sealed class FabricaPlata(IDefaultCommunicationTimeouts tiempos, MessageEncoder encoder)
        : ChannelFactoryBase<IRequestChannel>(tiempos)
    {
        protected override IRequestChannel OnCreateChannel(EndpointAddress direccion, Uri via)
            => new CanalPlata(this, encoder, direccion, via);

        protected override void OnOpen(TimeSpan timeout) { }
        protected override IAsyncResult OnBeginOpen(TimeSpan timeout, AsyncCallback callback, object state)
            => Apm.Completado(callback, state);
        protected override void OnEndOpen(IAsyncResult result) { }
    }

    private sealed class CanalPlata(ChannelManagerBase fabrica, MessageEncoder encoder, EndpointAddress direccion, Uri via)
        : ChannelBase(fabrica), IRequestChannel
    {
        public EndpointAddress RemoteAddress => direccion;
        public Uri Via => via;

        public IAsyncResult BeginRequest(Message message, AsyncCallback callback, object state)
            => BeginRequest(message, DefaultSendTimeout, callback, state);

        public IAsyncResult BeginRequest(Message message, TimeSpan timeout, AsyncCallback callback, object state)
            => Apm.Comenzar(PedirAsync(message), callback, state);

        public Message EndRequest(IAsyncResult result) => Apm.Terminar<Message>(result);

        public Message Request(Message message) => throw Sincronico();
        public Message Request(Message message, TimeSpan timeout) => throw Sincronico();

        private async Task<Message> PedirAsync(Message mensaje)
        {
            var enviar = Portero.Enviar ?? throw new CommunicationException("Plata: no hay salida de red (Portero).");
            // Como el transporte HTTP de WCF: sin WS-Addressing, la acción viaja solo en la
            // cabecera SOAPAction; el encabezado SOAP Action (mustUnderstand) se quita.
            var accion = mensaje.Headers.Action;
            if (mensaje.Version.Addressing == AddressingVersion.None)
            {
                mensaje.Headers.Action = null;
                mensaje.Headers.To = null;
            }

            // Siempre las versiones asincrónicas del encoder: las sincrónicas esperan con
            // WaitOne, que en el navegador (un solo hilo) gira para siempre.
            var cuerpo = new MemoryStream();
            await encoder.WriteMessageAsync(mensaje, cuerpo);
            var cabeceras = new List<(string, string)> { ("Content-Type", encoder.ContentType) };
            if (encoder.MessageVersion.Envelope == EnvelopeVersion.Soap11)
                cabeceras.Add(("SOAPAction", "\"" + accion + "\""));

            RespuestaHttp respuesta;
            try { respuesta = await enviar(new PedidoHttp("POST", via.AbsoluteUri, cabeceras, cuerpo.ToArray())); }
            catch (Exception e) when (e is not CommunicationException)
            {
                throw new CommunicationException($"Plata: falló el pedido a {via.GetLeftPart(UriPartial.Path)}: {e.Message}", e);
            }

            // 200 con la respuesta, o 500 con un fault de SOAP: los dos traen un mensaje.
            var tipo = respuesta.Cabeceras.FirstOrDefault(c => c.Nombre.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Valor;
            if ((respuesta.Estado == 200 || respuesta.Estado == 500) && respuesta.Cuerpo.Length > 0 && tipo is not null)
                return await encoder.ReadMessageAsync(new MemoryStream(respuesta.Cuerpo), int.MaxValue, tipo);

            var texto = $"Plata: {via.GetLeftPart(UriPartial.Path)} respondió HTTP {respuesta.Estado}.";
            throw respuesta.Estado == 404 ? new EndpointNotFoundException(texto) : new CommunicationException(texto);
        }

        private static NotSupportedException Sincronico()
            => new("Plata: el navegador solo admite pedidos asincrónicos (Begin/End).");

        protected override void OnAbort() { }
        protected override void OnClose(TimeSpan timeout) { }
        protected override IAsyncResult OnBeginClose(TimeSpan timeout, AsyncCallback callback, object state)
            => Apm.Completado(callback, state);
        protected override void OnEndClose(IAsyncResult result) { }
        protected override void OnOpen(TimeSpan timeout) { }
        protected override IAsyncResult OnBeginOpen(TimeSpan timeout, AsyncCallback callback, object state)
            => Apm.Completado(callback, state);
        protected override void OnEndOpen(IAsyncResult result) { }
    }
}

/// <summary>Puente entre tareas y el patrón Begin/End que usa WCF.</summary>
internal static class Apm
{
    public static IAsyncResult Comenzar<T>(Task<T> tarea, AsyncCallback? callback, object? estado)
    {
        var tcs = new TaskCompletionSource<T>(estado);
        tarea.ContinueWith(t =>
        {
            if (t.IsFaulted) tcs.TrySetException(t.Exception!.InnerExceptions);
            else if (t.IsCanceled) tcs.TrySetCanceled();
            else tcs.TrySetResult(t.Result);
            callback?.Invoke(tcs.Task);
        }, TaskScheduler.Default);
        return tcs.Task;
    }

    // Se llama desde el callback, con la tarea ya terminada: no bloquea.
    public static T Terminar<T>(IAsyncResult resultado) => ((Task<T>)resultado).GetAwaiter().GetResult();

    public static IAsyncResult Completado(AsyncCallback? callback, object? estado)
    {
        var tcs = new TaskCompletionSource<bool>(estado);
        tcs.SetResult(true);
        callback?.Invoke(tcs.Task);
        return tcs.Task;
    }
}
