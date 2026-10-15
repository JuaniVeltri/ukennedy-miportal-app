// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using System.Windows;

namespace Plata.Compat;

/// <summary>
/// Carga el XAML de una pantalla compilada para Silverlight sobre la instancia que ya existe
/// (el <c>Application.LoadComponent(this, uri)</c> de su <c>InitializeComponent</c>).
/// </summary>
/// <remarks>
/// El <c>LoadComponent</c> de OpenSilver busca una clase que genera su propio compilador de
/// XAML; las apps compiladas para Silverlight no la tienen, así que no carga nada y no avisa.
/// Acá se lee el XAML de <c>&lt;Ensamblado&gt;.g.resources</c> y se procesa con el lector de
/// XAML en tiempo de ejecución de OpenSilver, aplicándolo sobre la instancia (RootObjectInstance)
/// y registrando los x:Name en su ámbito de nombres para que funcione <c>FindName</c>.
/// </remarks>
public static class CargadorDeXaml
{
    internal const string MarcaDeOrigen = "plata-origen";
    private const string Presentacion = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    public static void Cargar(object componente, Uri ubicacion)
    {
        ArgumentNullException.ThrowIfNull(componente);
        ArgumentNullException.ThrowIfNull(ubicacion);

        var (nombre, ruta) = Ubicacion(ubicacion);
        var ensamblado = Ensamblado(nombre)
                         ?? throw new InvalidOperationException($"Plata: no está cargado el ensamblado '{nombre}' de '{ubicacion}'.");
        var xaml = LeerXaml(ensamblado, ruta)
                   ?? throw new InvalidOperationException($"Plata: no se encontró '{ruta}' en '{nombre}'.");

        var nombres = new NameScope();
        Aplicar(xaml, ensamblado, ruta, componente, nombres);
        if (componente is DependencyObject d) NameScope.SetNameScope(d, nombres);
        // Los templates de la pantalla se procesan al aplicarse y ya no ven sus Resources
        // (ver TemaPlata.Respaldar): se dejan como último recurso de búsqueda.
        if (componente is FrameworkElement conRecursos)
            TemaPlata.Respaldar(nombre + "/" + ruta, conRecursos.Resources);
        // OpenSilver resuelve las URIs relativas ("/Images/x.png") según el XAML del que vino
        // cada elemento; estas pantallas no tienen uno propio. Se las marca para que esas
        // rutas queden en plata://app/resources/plata-origen/..., que el host trae del sitio
        // de origen del .xap, como hace Silverlight.
        if (componente is UIElement e && string.IsNullOrEmpty(e.XamlSourcePath))
            e.XamlSourcePath = MarcaDeOrigen + "/" + ruta;
    }

    /// <summary>
    /// Un diccionario de recursos del XAML de un ensamblado (p. ej. "themes/generic.xaml"),
    /// o null si el ensamblado no lo tiene.
    /// </summary>
    public static ResourceDictionary? CargarDiccionario(Assembly ensamblado, string ruta)
    {
        var xaml = LeerXaml(ensamblado, ruta);
        if (xaml is null) return null;
        var diccionario = new ResourceDictionary();
        Aplicar(xaml, ensamblado, ruta, diccionario, new NameScope());
        return diccionario;
    }

    // Aplica el XAML sobre la raíz. OpenSilver solo guarda el Source de un ResourceDictionary
    // (su compilador lo resuelve antes); acá, al terminar cada diccionario con Source, se lo
    // llena con el XAML al que apunta, relativo al XAML que lo contiene.
    private static void Aplicar(string xaml, Assembly ensamblado, string ruta, object raiz, NameScope nombres)
    {
        var nombre = ensamblado.GetName().Name!;
        if (raiz is not ResourceDictionary)
            xaml = EventosEnTemplates(xaml, raiz.GetType(), Eventos.Registrar(raiz));
        var raizAnterior = Diferido.RaizActual;
        Diferido.RaizActual = raiz;
        try
        {
        // Un ResourceDictionary con Source se llena al terminar de asignarle las propiedades
        // (AfterProperties llega a todos los objetos; AfterEndInit solo a los ISupportInitialize).
        LectorDeOpenSilver.Aplicar(Adaptar(xaml), ensamblado, raiz, nombres, null, null, objeto =>
        {
            if (objeto is not ResourceDictionary { Source: { } fuente } rd || ReferenceEquals(rd, raiz)) return;
            var (nombreDestino, rutaDestino) = ResolverFuente(fuente, nombre, ruta);
            var destino = Ensamblado(nombreDestino);
            // Acá el diccionario todavía está en su BeginInit: el contenido se carga en uno nuevo
            // y se agrega como fusionado (las búsquedas lo recorren igual).
            var contenido = destino is null ? null : CargarDiccionario(destino, rutaDestino);
            if (contenido is null)
            {
                Avisos.Emitir($"diccionario no encontrado: {fuente} (desde {nombre}/{ruta})");
                return;
            }
            rd.MergedDictionaries.Add(contenido);
            TemaPlata.Respaldar(nombreDestino + "/" + rutaDestino, contenido);
        });
        }
        finally { Diferido.RaizActual = raizAnterior; }
    }

    /// <summary>
    /// Dónde está el XAML de un <c>ResourceDictionary Source</c>: "/Ensamblado;component/…" en ese
    /// ensamblado; "/…" desde la raíz del ensamblado actual; si no, relativo a la carpeta del XAML actual.
    /// </summary>
    internal static (string Ensamblado, string Ruta) ResolverFuente(Uri fuente, string ensamblado, string ruta)
    {
        var texto = fuente.OriginalString;
        if (texto.Contains(";component/", StringComparison.OrdinalIgnoreCase)) return Ubicacion(fuente);

        var relativa = Uri.UnescapeDataString(texto);
        var carpeta = relativa.StartsWith('/') ? "" : ruta[..(ruta.LastIndexOf('/') + 1)];
        var partes = new List<string>();
        foreach (var p in (carpeta + relativa.TrimStart('/')).Split('/'))
        {
            if (p == "..") { if (partes.Count > 0) partes.RemoveAt(partes.Count - 1); }
            else if (p.Length > 0 && p != ".") partes.Add(p);
        }
        return (ensamblado, string.Join('/', partes).ToLowerInvariant());
    }

    private static Assembly? Ensamblado(string nombre)
        => AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, nombre, StringComparison.OrdinalIgnoreCase));

    private static string? LeerXaml(Assembly ensamblado, string ruta)
    {
        using var recursos = ensamblado.GetManifestResourceStream(ensamblado.GetName().Name + ".g.resources");
        return recursos is null ? null : LeerDeRecursos(recursos, ruta);
    }

    /// <summary>"/Ensamblado;component/Ruta/Pantalla.xaml" → (Ensamblado, "ruta/pantalla.xaml").</summary>
    internal static (string Ensamblado, string Ruta) Ubicacion(Uri ubicacion)
    {
        var texto = ubicacion.OriginalString.TrimStart('/');
        const string marca = ";component/";
        var i = texto.IndexOf(marca, StringComparison.OrdinalIgnoreCase);
        if (i <= 0) throw new ArgumentException($"Plata: '{ubicacion}' no es una dirección de componente.", nameof(ubicacion));
        return (texto[..i], Uri.UnescapeDataString(texto[(i + marca.Length)..]).ToLowerInvariant());
    }

    internal static string? LeerDeRecursos(Stream recursos, string ruta)
    {
        using var lector = new ResourceReader(recursos);
        var e = lector.GetEnumerator();
        while (e.MoveNext())
        {
            if (!string.Equals((string)e.Key, ruta, StringComparison.OrdinalIgnoreCase)) continue;
            using var texto = e.Value switch
            {
                Stream s => new StreamReader(s),
                byte[] b => new StreamReader(new MemoryStream(b)),
                _ => null,
            };
            return texto?.ReadToEnd() ?? e.Value as string;
        }
        return null;
    }

    private static readonly Regex XmlnsDeSilverlight = new(
        "\"clr-namespace:[^;\"]+;assembly=System\\.Windows(\\.[A-Za-z.]+)?\"", RegexOptions.CultureInvariant);

    private static readonly Regex XClass = new(
        "\\s+x:Class(Modifier)?=\"[^\"]*\"", RegexOptions.CultureInvariant);

    /// <summary>
    /// Ajusta el XAML de Silverlight para el lector de OpenSilver:
    /// los espacios <c>clr-namespace:…;assembly=System.Windows*</c> apuntan a ensamblados de
    /// Silverlight que no existen acá; OpenSilver publica esos tipos en el espacio de
    /// presentación (repartidos en varios ensamblados), así que se usa ese. Y se quita
    /// <c>x:Class</c>, que es para el compilador.
    /// </summary>
    internal static string Adaptar(string xaml)
    {
        xaml = XmlnsDeSilverlight.Replace(xaml, "\"" + Presentacion + "\"");
        xaml = XClass.Replace(xaml, "");
        // Loaded="X" → plata:Diferido.Cargado="X" (Loaded con la semántica de Silverlight).
        xaml = AtributoLoaded.Replace(xaml, "$1plata:Diferido.Cargado=");
        // VisualStateManager.VisualStateGroups → plata:Estados.Grupos (ver Estados).
        xaml = GruposDeEstados.Replace(xaml, "<$1plata:Estados.Grupos>");
        xaml = NombresComoClaves(xaml);
        xaml = MarcarBindingsOneTime(xaml);
        xaml = ContenidoImplicito(xaml);
        xaml = OrigenConEfecto(xaml);
        if (xaml.Contains("plata:", StringComparison.Ordinal) && !xaml.Contains("xmlns:plata=", StringComparison.Ordinal))
        {
            var raiz = PrimeraEtiqueta.Match(xaml);
            xaml = xaml.Insert(raiz.Index + raiz.Length, " xmlns:plata=\"clr-namespace:Plata.Compat;assembly=Plata.Compat\"");
        }
        // Al final, para que los templates también lleven xmlns:plata.
        return LlevarXmlnsATemplates(xaml);
    }

    // Dentro de templates, Evento="Handler" (Handler: método (sender, EventArgs) de la raíz) pasa a
    // plata:Eventos.Conectar="id|Evento:Handler;…" (ver Eventos). Fuera de templates no se toca.
    internal static string EventosEnTemplates(string xaml, Type tipoRaiz, int idRaiz)
    {
        var padres = new Stack<(string Nombre, bool EnTemplate)>();
        var cambios = new List<(int Indice, int Largo, string Nueva)>();
        foreach (Match t in Etiqueta.Matches(xaml))
        {
            var nombre = t.Groups[2].Value;
            if (t.Groups[1].Value == "/") { if (padres.Count > 0) padres.Pop(); continue; }
            var enTemplate = padres.Count > 0 && padres.Peek().EnTemplate;
            if (enTemplate)
            {
                var pares = new List<string>();
                var nueva = AtributoSimple.Replace(t.Value, a =>
                {
                    if (Eventos.Metodo(tipoRaiz, a.Groups[2].Value) is null) return a.Value;
                    pares.Add(a.Groups[1].Value + ":" + a.Groups[2].Value);
                    return "";
                });
                if (pares.Count > 0)
                {
                    var cierre = nueva.EndsWith("/>", StringComparison.Ordinal) ? 2 : 1;
                    nueva = nueva[..^cierre] + $" plata:Eventos.Conectar=\"{idRaiz}|{string.Join(";", pares)}\"" + nueva[^cierre..];
                    cambios.Add((t.Index, t.Length, nueva));
                }
            }
            if (!t.Value.EndsWith("/>", StringComparison.Ordinal))
                padres.Push((nombre, enTemplate || EsTemplate(nombre)));
        }
        foreach (var (indice, largo, nueva) in cambios.OrderByDescending(c => c.Indice))
            xaml = xaml.Remove(indice, largo).Insert(indice, nueva);
        return xaml;
    }

    private static bool EsTemplate(string etiqueta)
    {
        var local = etiqueta[(etiqueta.IndexOf(':') + 1)..];
        return local is "ControlTemplate" or "DataTemplate" or "ItemsPanelTemplate" or "HierarchicalDataTemplate";
    }

    // Atributo sin prefijo ni punto con un identificador como valor: candidato a Evento="Handler".
    private static readonly Regex AtributoSimple = new("\\s([A-Za-z]\\w*)=\"([A-Za-z_]\\w*)\"", RegexOptions.CultureInvariant);

    // En Silverlight, un ContentPresenter (o ScrollContentPresenter) sin Content dentro del template
    // de un control toma solo el Content y el ContentTemplate del control; OpenSilver no, y queda
    // vacío (p. ej. la lista de un ListBox con un ScrollViewer propio). Se agregan los TemplateBinding.
    internal static string ContenidoImplicito(string xaml)
    {
        var espacios = EspaciosClr(xaml);
        var templates = new Stack<bool>(); // por cada template abierto: ¿es de un ContentControl?
        var padres = new Stack<string>();
        var cambios = new List<(int Indice, int Largo, string Nueva)>();
        var etiquetas = Etiqueta.Matches(xaml);
        for (var k = 0; k < etiquetas.Count; k++)
        {
            var t = etiquetas[k];
            var nombre = t.Groups[2].Value;
            var local = nombre[(nombre.IndexOf(':') + 1)..];
            if (t.Groups[1].Value == "/")
            {
                if (padres.Count > 0 && EsTemplate(padres.Pop()) && templates.Count > 0) templates.Pop();
                continue;
            }
            var cierraSolo = t.Value.EndsWith("/>", StringComparison.Ordinal);
            // Sin Content, y sin hijos (si los tiene, esos son el contenido).
            var sinHijos = cierraSolo || (k + 1 < etiquetas.Count && etiquetas[k + 1].Groups[1].Value == "/");
            if (templates.Count > 0 && templates.Peek() && local is "ContentPresenter" or "ScrollContentPresenter"
                && sinHijos && !Regex.IsMatch(t.Value, "\\sContent="))
            {
                var agregar = " Content=\"{TemplateBinding Content}\"";
                if (!Regex.IsMatch(t.Value, "\\sContentTemplate=")) agregar += " ContentTemplate=\"{TemplateBinding ContentTemplate}\"";
                var cierre = cierraSolo ? 2 : 1;
                cambios.Add((t.Index, t.Length, t.Value[..^cierre] + agregar + t.Value[^cierre..]));
            }
            if (cierraSolo) continue;
            padres.Push(nombre);
            if (EsTemplate(nombre))
                templates.Push(local == "ControlTemplate" && EsDeContentControl(TipoDestino.Match(t.Value), espacios));
        }
        foreach (var (indice, largo, nueva) in cambios.OrderByDescending(c => c.Indice))
            xaml = xaml.Remove(indice, largo).Insert(indice, nueva);
        return xaml;
    }

    private static readonly Regex TipoDestino = new("\\sTargetType=\"(?:\\{x:Type\\s+)?(?:(\\w+):)?(\\w+)\\}?\"", RegexOptions.CultureInvariant);
    private static readonly Regex EspacioClr = new("xmlns:(\\w+)=\"clr-namespace:([^;\"]+)", RegexOptions.CultureInvariant);
    private static readonly Dictionary<string, bool> EsContentControlCache = new();

    private static Dictionary<string, string> EspaciosClr(string xaml)
    {
        var d = new Dictionary<string, string>();
        foreach (Match m in EspacioClr.Matches(xaml)) d[m.Groups[1].Value] = m.Groups[2].Value;
        return d;
    }

    // ¿El TargetType del template es un ContentControl? Sin TargetType o sin resolver: no.
    private static bool EsDeContentControl(Match destino, Dictionary<string, string> espacios)
    {
        if (!destino.Success) return false;
        var prefijo = destino.Groups[1].Value;
        var nombre = destino.Groups[2].Value;
        string[] posibles = prefijo.Length > 0 && espacios.TryGetValue(prefijo, out var ns)
            ? new[] { ns }
            : new[] { "System.Windows.Controls", "System.Windows.Controls.Primitives" };
        var clave = string.Join("|", posibles) + "." + nombre;
        lock (EsContentControlCache)
        {
            if (EsContentControlCache.TryGetValue(clave, out var r)) return r;
            var contentControl = typeof(System.Windows.Controls.ContentControl); // carga OpenSilver antes de recorrer
            r = AppDomain.CurrentDomain.GetAssemblies().Any(a =>
                posibles.Any(p => a.GetType(p + "." + nombre, throwOnError: false) is { } tipo &&
                                  contentControl.IsAssignableFrom(tipo)));
            return EsContentControlCache[clave] = r;
        }
    }

    // Elementos con atributos {Binding … Mode=OneTime …}: se agrega plata:Enlaces.Refrescar con
    // esas propiedades, para volver a evaluarlas al cargarse (ver Enlaces).
    internal static string MarcarBindingsOneTime(string xaml)
        => EtiquetaConAtributos.Replace(xaml, t =>
        {
            if (t.Groups[1].Value.Contains('.')) return t.Value; // elemento de propiedad
            var props = BindingOneTime.Matches(t.Value).Select(m => m.Groups[1].Value).Distinct().ToList();
            if (props.Count == 0) return t.Value;
            var cierre = t.Value.EndsWith("/>", StringComparison.Ordinal) ? 2 : 1;
            return t.Value[..^cierre] + $" plata:Enlaces.Refrescar=\"{string.Join(",", props)}\"" + t.Value[^cierre..];
        });

    private static readonly Regex EtiquetaConAtributos = new("<(?![/!?])([\\w:.]+)\\s[^>]*>", RegexOptions.CultureInvariant);
    // Atributo simple (sin prefijo ni punto) cuyo valor es un {Binding …} con Mode=OneTime.
    private static readonly Regex BindingOneTime = new(
        "\\s([A-Za-z]\\w*)=\"\\{Binding[^\"]*Mode=OneTime[^\"]*\"", RegexOptions.CultureInvariant);

    // Silverlight dibuja la RenderTransform de un elemento con Effect desde su esquina, sin el
    // RenderTransformOrigin. Evidencia: el rótulo vertical del menú de GGI (TextBlock con
    // DropShadowEffect, origen 0.5,0.5, rotación de 90° y traslación 18,5) sale en Silverlight sobre
    // la franja de 20 px desde arriba, que es lo que dan las cuentas con origen 0,0; con el origen
    // en el centro queda a 80 px y cortado arriba. Se aplica solo a esa combinación (Effect +
    // RenderTransform + origen distinto de 0,0).
    internal static string OrigenConEfecto(string xaml)
    {
        var padres = new Stack<ElementoTransformado>();
        var cambios = new List<Group>();
        foreach (Match t in Etiqueta.Matches(xaml))
        {
            var nombre = t.Groups[2].Value;
            if (t.Groups[1].Value == "/")
            {
                if (padres.Count > 0 && padres.Pop() is { Origen: { } o, ConEfecto: true, ConTransformacion: true }) cambios.Add(o);
                continue;
            }
            // <TextBlock.Effect>, <TextBlock.RenderTransform>: propiedades del elemento abierto.
            if (padres.Count > 0 && nombre.StartsWith(padres.Peek().Nombre + ".", StringComparison.Ordinal))
            {
                var propiedad = nombre[(padres.Peek().Nombre.Length + 1)..];
                padres.Peek().ConEfecto |= propiedad == "Effect";
                padres.Peek().ConTransformacion |= propiedad == "RenderTransform";
            }
            var origen = OrigenDeTransformacion.Match(xaml, t.Index, t.Length);
            var e = new ElementoTransformado(nombre)
            {
                Origen = origen.Success && origen.Groups[1].Value.Replace(" ", "") is not ("0,0" or "0.0,0.0") ? origen.Groups[1] : null,
                ConEfecto = t.Value.Contains(" Effect=", StringComparison.Ordinal),
                ConTransformacion = t.Value.Contains(" RenderTransform=", StringComparison.Ordinal),
            };
            if (!t.Value.EndsWith("/>", StringComparison.Ordinal)) padres.Push(e);
            else if (e is { Origen: { } o, ConEfecto: true, ConTransformacion: true }) cambios.Add(o);
        }
        foreach (var o in cambios.OrderByDescending(c => c.Index))
            xaml = xaml.Remove(o.Index, o.Length).Insert(o.Index, "0,0");
        return xaml;
    }

    private sealed class ElementoTransformado(string nombre)
    {
        public string Nombre { get; } = nombre;
        public Group? Origen { get; init; }
        public bool ConEfecto { get; set; }
        public bool ConTransformacion { get; set; }
    }

    private static readonly Regex OrigenDeTransformacion = new("\\sRenderTransformOrigin=\"([^\"]*)\"", RegexOptions.CultureInvariant);

    // En Silverlight, dentro de un diccionario de recursos x:Name también es la clave
    // (<Style x:Name="s"> se usa como {StaticResource s}). Se agrega x:Key a esos elementos.
    internal static string NombresComoClaves(string xaml)
    {
        var padres = new Stack<string>();
        var inserciones = new List<int>();
        foreach (Match t in Etiqueta.Matches(xaml))
        {
            var nombre = t.Groups[2].Value;
            if (t.Groups[1].Value == "/") { if (padres.Count > 0) padres.Pop(); continue; }
            var padre = padres.Count > 0 ? padres.Peek() : "";
            var esRecurso = padre.EndsWith(".Resources", StringComparison.Ordinal) ||
                            padre == "ResourceDictionary" || padre.EndsWith(":ResourceDictionary", StringComparison.Ordinal);
            if (esRecurso && t.Value.Contains("x:Name=", StringComparison.Ordinal) && !t.Value.Contains("x:Key=", StringComparison.Ordinal))
                inserciones.Add(t.Index);
            if (!t.Value.EndsWith("/>", StringComparison.Ordinal)) padres.Push(nombre);
        }
        foreach (var i in inserciones.OrderByDescending(i => i))
        {
            var etiqueta = Etiqueta.Match(xaml, i);
            var valor = XName.Match(etiqueta.Value).Groups[1].Value;
            var tras = i + 1 + etiqueta.Groups[2].Length;
            xaml = xaml.Insert(tras, $" x:Key=\"{valor}\"");
        }
        return xaml;
    }

    // Etiquetas de inicio/fin (sin comentarios, declaraciones ni CDATA).
    private static readonly Regex Etiqueta = new("<(/?)([\\w:.]+)(?:\\s[^>]*)?>", RegexOptions.CultureInvariant);
    private static readonly Regex XName = new("x:Name=\"([^\"]*)\"", RegexOptions.CultureInvariant);

    // El contenido de un template se procesa recién al aplicarse, y OpenSilver lo vuelve a leer
    // sin las declaraciones xmlns de la raíz: un tipo sin prefijo ("ToggleButton") ya no se
    // resuelve. Se copian esas declaraciones al primer elemento del contenido de cada template.
    internal static string LlevarXmlnsATemplates(string xaml)
    {
        var raiz = EtiquetaDeInicio.Match(xaml);
        if (!raiz.Success) return xaml;
        var declaraciones = DeclaracionXmlns.Matches(raiz.Value).Select(m => (Prefijo: m.Groups[1].Value, Texto: m.Value.Trim())).ToList();
        if (declaraciones.Count == 0) return xaml;

        var inserciones = new List<(int Indice, string Texto)>();
        foreach (Match plantilla in InicioDePlantilla.Matches(xaml))
        {
            if (plantilla.Value.EndsWith("/>", StringComparison.Ordinal)) continue;
            // Primer elemento del contenido (no un elemento de propiedad "<X.Y>").
            for (var m = EtiquetaDeInicio.Match(xaml, plantilla.Index + plantilla.Length); m.Success; m = m.NextMatch())
            {
                if (m.Groups[1].Value.Contains('.')) continue;
                var propias = DeclaracionXmlns.Matches(m.Value).Select(d => d.Groups[1].Value).ToHashSet();
                var faltan = declaraciones.Where(d => !propias.Contains(d.Prefijo)).Select(d => d.Texto).ToList();
                if (faltan.Count > 0)
                    inserciones.Add((m.Index + 1 + m.Groups[1].Length, " " + string.Join(" ", faltan)));
                break;
            }
        }
        foreach (var (indice, texto) in inserciones.OrderByDescending(i => i.Indice))
            xaml = xaml.Insert(indice, texto);
        return xaml;
    }

    private static readonly Regex EtiquetaDeInicio = new("<(?![/!?])([\\w:.]+)[^>]*>", RegexOptions.CultureInvariant);
    private static readonly Regex DeclaracionXmlns = new("\\sxmlns(:[\\w.-]+)?=\"[^\"]*\"", RegexOptions.CultureInvariant);
    private static readonly Regex InicioDePlantilla = new(
        "<(?:\\w+:)?(?:ControlTemplate|DataTemplate|ItemsPanelTemplate|HierarchicalDataTemplate)(?=[\\s>/])[^>]*>",
        RegexOptions.CultureInvariant);

    private static readonly Regex GruposDeEstados = new(
        "<(/?)(?:[\\w]+:)?VisualStateManager\\.VisualStateGroups\\s*>", RegexOptions.CultureInvariant);

    private static readonly Regex AtributoLoaded = new("(\\s)Loaded=(?=\")", RegexOptions.CultureInvariant);
    // La etiqueta del elemento raíz (sin la declaración <?xml ?> ni comentarios).
    private static readonly Regex PrimeraEtiqueta = new("<(?![?!])[\\w:.]+", RegexOptions.CultureInvariant);
}

/// <summary>
/// Acceso al lector/escritor de XAML de OpenSilver (System.Xaml portado, interno a OpenSilver):
/// lo mismo que hace <c>System.Windows.Markup.XamlReader.Load</c>, pero con las opciones
/// que esa API no expone (instancia raíz, ensamblado local y ámbito de nombres).
/// </summary>
internal static class LectorDeOpenSilver
{
    private static readonly Assembly OpenSilver = typeof(System.Windows.Markup.XamlReader).Assembly;
    private static readonly Type TLector = Tipo("System.Xaml.XamlXmlReader");
    private static readonly Type TOpcionesLector = Tipo("System.Xaml.XamlXmlReaderSettings");
    private static readonly Type TEscritor = Tipo("System.Xaml.XamlObjectWriter");
    private static readonly Type TOpcionesEscritor = Tipo("System.Xaml.XamlObjectWriterSettings");
    private static readonly Type TLectorBase = Tipo("System.Xaml.XamlReader");

    private static readonly object Contexto = typeof(System.Windows.Markup.XamlReader)
        .GetProperty("SharedXamlContext", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    public static void Aplicar(string xaml, Assembly local, object raiz, System.Windows.Markup.INameScope nombres,
        Action<object>? alTerminarObjeto = null, Action<object>? antesDePropiedades = null,
        Action<object>? despuesDePropiedades = null)
    {
        var opcionesLector = Activator.CreateInstance(TOpcionesLector)!;
        Poner(opcionesLector, "LocalAssembly", local);
        Poner(opcionesLector, "ProvideLineInfo", true);

        var opcionesEscritor = Activator.CreateInstance(TOpcionesEscritor)!;
        Poner(opcionesEscritor, "RootObjectInstance", raiz);
        Poner(opcionesEscritor, "ExternalNameScope", nombres);
        Poner(opcionesEscritor, "RegisterNamesOnExternalNamescope", true);
        Enganchar(opcionesEscritor, "AfterEndInitHandler", alTerminarObjeto);
        Enganchar(opcionesEscritor, "BeforePropertiesHandler", antesDePropiedades);
        Enganchar(opcionesEscritor, "AfterPropertiesHandler", despuesDePropiedades);

        var lector = Activator.CreateInstance(TLector, new StringReader(xaml), Contexto, opcionesLector)!;
        var escritor = Activator.CreateInstance(TEscritor, Contexto, opcionesEscritor)!;

        var leer = TLectorBase.GetMethod("Read")!;
        var tipoNodo = TLectorBase.GetProperty("NodeType")!;
        var fin = TLectorBase.GetProperty("IsEof")!;
        var escribir = TEscritor.GetMethod("WriteNode", new[] { TLectorBase })!;
        var linea = TLector.GetProperty("LineNumber");
        var columna = TLector.GetProperty("LinePosition");
        var ponerLinea = TEscritor.GetMethod("SetLineInfo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         ?? TEscritor.GetInterfaces().Select(i => i.GetMethod("SetLineInfo")).FirstOrDefault(m => m is not null);

        if ((int)tipoNodo.GetValue(lector)! == 0) leer.Invoke(lector, null); // XamlNodeType.None
        while (!(bool)fin.GetValue(lector)!)
        {
            if (ponerLinea is not null && linea is not null && columna is not null)
                ponerLinea.Invoke(escritor, new[] { linea.GetValue(lector), columna.GetValue(lector) });
            try { escribir.Invoke(escritor, new[] { lector }); }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
            leer.Invoke(lector, null);
        }
    }

    // Los ganchos son EventHandler<XamlObjectEventArgs>, de un tipo interno: se enlaza un
    // método (object, EventArgs), que el delegado admite por contravarianza.
    private static void Enganchar(object opciones, string gancho, Action<object>? accion)
    {
        if (accion is null) return;
        var propiedad = TOpcionesEscritor.GetProperty(gancho)!;
        propiedad.SetValue(opciones, Delegate.CreateDelegate(
            propiedad.PropertyType, new Puente(accion), typeof(Puente).GetMethod(nameof(Puente.Manejar))!));
    }

    private sealed class Puente(Action<object> accion)
    {
        public void Manejar(object? emisor, EventArgs e)
        {
            if (e.GetType().GetProperty("Instance")?.GetValue(e) is { } instancia) accion(instancia);
        }
    }

    private static Type Tipo(string nombre)
        => OpenSilver.GetType(nombre, throwOnError: true)!;

    private static void Poner(object o, string propiedad, object valor)
        => o.GetType().GetProperty(propiedad)!.SetValue(o, valor);
}
