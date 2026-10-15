// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization.Json;
using System.Windows;

namespace Plata.Compat;

/// <summary>
/// Reemplazos de llamadas cuyo comportamiento en .NET moderno difiere del de Silverlight.
/// El traductor cambia cada llamada original por el método de acá con el mismo efecto que
/// tenía en Silverlight. Los métodos de instancia reciben la instancia como primer argumento.
/// </summary>
public static class Desvios
{
    /// <summary>
    /// <c>DataContractJsonSerializer.ReadObject(Stream)</c>. Las apps de Silverlight escriben
    /// en un <see cref="MemoryStream"/> y lo leen sin volver a la posición 0, y en Silverlight
    /// eso funcionaba. En .NET moderno se lee desde la posición actual (el final) y falla.
    /// Solo se rebobina un stream que quedó justo al final; si no, se respeta la posición.
    /// </summary>
    public static object? LeerJsonDesdeElPrincipio(DataContractJsonSerializer serializador, Stream stream)
    {
        if (stream.CanSeek && stream.Length > 0 && stream.Position == stream.Length)
            stream.Position = 0;
        return serializador.ReadObject(stream);
    }

    /// <summary>
    /// <c>AppDomain.DefineDynamicAssembly(AssemblyName, AssemblyBuilderAccess)</c>: en .NET
    /// moderno es el estático <see cref="AssemblyBuilder.DefineDynamicAssembly(AssemblyName, AssemblyBuilderAccess)"/>.
    /// </summary>
    public static AssemblyBuilder DefinirEnsambladoDinamico(AppDomain dominio, AssemblyName nombre, AssemblyBuilderAccess acceso)
        => AssemblyBuilder.DefineDynamicAssembly(nombre, acceso);

    /// <summary>
    /// <c>AssemblyBuilder.DefineDynamicModule(string, bool emitSymbolInfo)</c>: la sobrecarga con
    /// símbolos no existe en .NET moderno; los símbolos no hacen falta para ejecutar.
    /// </summary>
    public static ModuleBuilder DefinirModuloDinamico(AssemblyBuilder ensamblado, string nombre, bool simbolos)
        => ensamblado.DefineDynamicModule(nombre);

    /// <summary>
    /// <c>new ChannelFactory&lt;T&gt;(binding, dirección)</c>. Si hay <see cref="Portero"/>, el
    /// transporte HTTP de la binding se cambia por <see cref="TransportePlata"/>; el resto de
    /// los elementos (codificación y los que agregue la app) quedan igual.
    /// </summary>
    public static System.ServiceModel.ChannelFactory<T> NuevaFabrica<T>(
        System.ServiceModel.Channels.Binding binding, System.ServiceModel.EndpointAddress direccion)
    {
        if (Portero.Enviar is null) return new System.ServiceModel.ChannelFactory<T>(binding, direccion);

        var elementos = binding.CreateBindingElements();
        for (var i = 0; i < elementos.Count; i++)
            if (elementos[i] is System.ServiceModel.Channels.HttpTransportBindingElement http)
                elementos[i] = new TransportePlata(http.Scheme, http.MaxReceivedMessageSize);
        var propia = new System.ServiceModel.Channels.CustomBinding(elementos)
        {
            Name = binding.Name,
            Namespace = binding.Namespace,
            OpenTimeout = binding.OpenTimeout,
            CloseTimeout = binding.CloseTimeout,
            SendTimeout = binding.SendTimeout,
            ReceiveTimeout = binding.ReceiveTimeout,
        };
        return new System.ServiceModel.ChannelFactory<T>(propia, direccion);
    }

    /// <summary>
    /// <c>ChannelFactory&lt;T&gt;.CreateChannel()</c>. Abre la fábrica de forma sincrónica, y el
    /// cliente WCF de .NET lo hace esperando una tarea, cosa que el navegador (un solo hilo) no
    /// permite. Se devuelve un <see cref="CanalDiferido"/>, que abre todo en forma asincrónica
    /// en la primera llamada.
    /// </summary>
    public static T CrearCanal<T>(System.ServiceModel.ChannelFactory<T> fabrica)
        => fabrica.State == System.ServiceModel.CommunicationState.Opened
            ? fabrica.CreateChannel()
            : CanalDiferido.Crear(fabrica);

    /// <summary>
    /// <c>ICommunicationObject.Close()</c>: también espera bloqueando. En HTTP no hay sesión que
    /// cerrar, así que abortar es equivalente y no bloquea.
    /// </summary>
    public static void Cerrar(System.ServiceModel.ICommunicationObject objeto) => objeto.Abort();

    /// <summary><c>CommunicationObject.Close()</c>: igual que <see cref="Cerrar"/>.</summary>
    public static void CerrarObjeto(System.ServiceModel.Channels.CommunicationObject objeto) => objeto.Abort();

    /// <summary>
    /// <c>ScriptObject.Invoke(string, object[])</c>. Las apps llaman a funciones JavaScript de la
    /// página que las contenía (p. ej. GGI: <c>KeepAliveWebSession</c>), que en Plata no existe.
    /// Si la función no está definida se avisa (para el informe) y se devuelve null en vez de fallar.
    /// </summary>
    public static object? InvocarEnPagina(System.Windows.Browser.ScriptObject objeto, string nombre, object[] args)
    {
        if (objeto is System.Windows.Browser.HtmlWindow && objeto.GetProperty(nombre) is null)
        {
            Avisos.Emitir("función de la página no definida: " + nombre);
            return null;
        }
        return objeto.Invoke(nombre, args);
    }

    /// <summary>
    /// <c>FrameworkElement.ActualHeight</c>. En Silverlight, <c>Loaded</c> llega con el layout ya
    /// hecho y las apps leen ahí los tamaños (p. ej. GGI ajusta el alto del menú al del padre);
    /// OpenSilver lo dispara antes de medir y el tamaño da 0. Si da 0, se hace el layout pendiente.
    /// </summary>
    public static double AltoReal(FrameworkElement elemento)
    {
        if (elemento.ActualHeight == 0) elemento.UpdateLayout();
        return elemento.ActualHeight;
    }

    /// <summary><c>FrameworkElement.ActualWidth</c>: igual que <see cref="AltoReal"/>.</summary>
    public static double AnchoReal(FrameworkElement elemento)
    {
        if (elemento.ActualWidth == 0) elemento.UpdateLayout();
        return elemento.ActualWidth;
    }

    /// <summary><c>HtmlPage.Window.Navigate(Uri)</c>: la abre el host (ver <see cref="Ventanas"/>).</summary>
    public static void Navegar(System.Windows.Browser.HtmlWindow ventana, Uri direccion) => Ventanas.Pedir(direccion);

    /// <summary>
    /// <c>HtmlPage.Window.Navigate(Uri, string destino)</c>: en Silverlight devuelve la ventana; en
    /// OpenSilver no existe con esa firma. La abre el host; no hay ventana que devolver.
    /// </summary>
    public static System.Windows.Browser.HtmlWindow? NavegarEn(System.Windows.Browser.HtmlWindow ventana, Uri direccion, string destino)
    {
        Ventanas.Pedir(direccion);
        return null;
    }

    /// <summary><c>HtmlPage.Window.Navigate(Uri, string, string)</c>: igual que <see cref="NavegarEn"/>.</summary>
    public static System.Windows.Browser.HtmlWindow? NavegarEnCon(System.Windows.Browser.HtmlWindow ventana, Uri direccion, string destino, string opciones)
    {
        Ventanas.Pedir(direccion);
        return null;
    }

    /// <summary><c>HtmlPage.PopupWindow(Uri, string, HtmlPopupWindowOptions)</c>: la abre el host.</summary>
    public static System.Windows.Browser.HtmlWindow? AbrirVentana(Uri direccion, string destino, System.Windows.Browser.HtmlPopupWindowOptions opciones)
    {
        Ventanas.Pedir(direccion);
        return null;
    }

    /// <summary>
    /// <c>Application.Current.Host.Source</c>: en Silverlight es la dirección del .xap (las apps
    /// arman otras direcciones a partir de ella); en OpenSilver, el origen de la página.
    /// </summary>
    public static Uri FuenteDelHost(System.Windows.Host host) => Origen.Xap ?? host.Source;

    /// <summary>
    /// <c>Application.GetResourceStream(StreamResourceInfo, Uri)</c>: una entrada de un paquete
    /// (.xap, que es un ZIP). En OpenSilver devuelve null.
    /// </summary>
    public static System.Windows.Resources.StreamResourceInfo? LeerDelPaquete(
        System.Windows.Resources.StreamResourceInfo paquete, Uri ubicacion)
    {
        var stream = paquete.Stream;
        if (stream.CanSeek) stream.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
        var nombre = Uri.UnescapeDataString(ubicacion.OriginalString).TrimStart('/').Replace('\\', '/');
        var entrada = zip.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName.Replace('\\', '/'), nombre, StringComparison.OrdinalIgnoreCase));
        if (entrada is null) return null;
        var copia = new MemoryStream();
        using (var origen = entrada.Open()) origen.CopyTo(copia);
        copia.Position = 0;
        return new System.Windows.Resources.StreamResourceInfo(copia, null!);
    }

    /// <summary>
    /// <c>AssemblyPart.Load(Stream)</c>: carga una librería que la app bajó (p. ej. un módulo).
    /// En OpenSilver devuelve null; acá pasa por el mismo traductor que las del .xap.
    /// </summary>
    public static Assembly? CargarParte(System.Windows.AssemblyPart parte, Stream stream)
    {
        var copia = new MemoryStream();
        stream.CopyTo(copia);
        var cargar = Ensamblados.Cargar ?? throw new InvalidOperationException("Plata: no hay cargador de librerías.");
        return cargar(copia.ToArray());
    }

    /// <summary>
    /// <c>Application.LoadComponent(object, Uri)</c>: en OpenSilver no carga el XAML de las
    /// apps compiladas para Silverlight (ver <see cref="CargadorDeXaml"/>).
    /// </summary>
    public static void CargarComponente(object componente, Uri ubicacion)
        => CargadorDeXaml.Cargar(componente, ubicacion);
}
