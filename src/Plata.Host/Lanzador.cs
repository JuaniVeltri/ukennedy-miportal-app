// SPDX-License-Identifier: MIT
using System.Net;
using System.Text.RegularExpressions;

namespace Plata.Host;

/// <summary>
/// Hace lo que hacía el visor de Windows de la universidad con un enlace <c>uksv:</c>: pide la
/// página del lanzador (con <c>&amp;v=3</c>), sigue sus redirecciones por JavaScript hasta la página
/// que contiene el objeto Silverlight y saca de ella el .xap y los InitParams. Todo pasa por el
/// Portero, así la sesión que abre el lanzador (cookies) queda para la app.
/// </summary>
public sealed class Lanzador
{
    public const string Esquema = "uksv";

    /// <summary>
    /// Corre en las páginas del portal. Antes de abrir una app, el portal pregunta si el visor está
    /// instalado con <c>checkUKSVCookie()</c>, que busca la cookie <c>uksvCookie</c> (la pone su
    /// botón "Si ya instalaste el visor hacé clic aquí"). En Plata el visor está, pero el portal
    /// corre en un marco y el navegador no le deja leer ni escribir cookies desde JavaScript: se
    /// responde directo. Además avisa a la ventana apenas se hace clic en una app (el enlace llega
    /// recién después de la respuesta del servidor) y le pasa el enlace <c>uksv:</c> en cuanto el
    /// portal lo pone en su iframe: así no depende de que el navegador cargue un esquema propio
    /// dentro de una página https (en macOS lo bloquea). En el formulario de inicio de sesión agrega
    /// "Recordarme en este equipo" y, si hay una cuenta recordada, "Iniciar sesión como …" (la cuenta
    /// la guarda el host en el llavero del sistema, ver <see cref="Credenciales"/>). Solo corre en
    /// páginas web: no en la ventana ni en las pestañas de las apps.
    /// </summary>
    public const string ScriptDelPortal = """
        (function () {
          if (!/^https?:$/.test(location.protocol)) return;
          if (typeof checkUKSVCookie === 'function') window.checkUKSVCookie = function () { return true; };
          if (window.parent === window) return;
          document.addEventListener('click', function (e) {
            var a = e.target && e.target.closest && e.target.closest('a[onclick*="checkConditions"]');
            if (a) window.parent.postMessage({ plataPortal: 'abriendo' }, '*');
          }, true);
          function revisar(nodo) {
            if (!nodo || nodo.nodeType !== 1) return;
            var marcos = nodo.tagName === 'IFRAME' ? [nodo] : nodo.querySelectorAll('iframe');
            for (var i = 0; i < marcos.length; i++) {
              var src = marcos[i].getAttribute('src') || '';
              if (/^uksv:/i.test(src)) window.parent.postMessage({ plataUksv: src }, '*');
            }
          }
          new MutationObserver(function (cambios) {
            cambios.forEach(function (c) {
              if (c.type === 'attributes') revisar(c.target);
              else for (var i = 0; i < c.addedNodes.length; i++) revisar(c.addedNodes[i]);
            });
          }).observe(document.documentElement, { subtree: true, childList: true, attributes: true, attributeFilter: ['src'] });

          var usuario = document.getElementById('ctl00_login_txtUser');
          var clave = document.getElementById('ctl00_login_txtPasswd');
          var entrar = document.getElementById('ctl00_login_btnEnviar');
          if (!usuario || !clave || !entrar) return;
          window.addEventListener('message', function (e) {
            if (e.source !== window.parent || !e.data || !e.data.plataCuenta) return;
            var c = e.data.plataCuenta;
            if (c.tipo === 'cuenta' && c.disponible) recordarme(c.usuario || null);
            if (c.tipo === 'cuenta-clave' && c.usuario && c.clave) {
              usuario.value = c.usuario;
              clave.value = c.clave;
              entrar.click();
            }
          });
          window.parent.postMessage({ plataCuenta: 'consultar' }, '*');

          function recordarme(guardado) {
            var tabla = entrar.closest('table');
            var celda = tabla.insertRow(-1).insertCell(0);
            // Estilos propios con !important: los del portal (pensados para sus campos) no los pisan.
            celda.innerHTML = '<label style="display:flex!important;align-items:center!important;justify-content:flex-start!important;' +
              'gap:8px!important;width:auto!important;margin:12px 0 0!important;padding:0!important;font-size:13px!important;' +
              'font-weight:normal!important;line-height:1.2!important;color:#3a3f45!important;cursor:pointer!important">' +
              '<input type="checkbox" style="-webkit-appearance:checkbox!important;appearance:auto!important;width:15px!important;' +
              'height:15px!important;min-width:0!important;max-width:15px!important;margin:0!important;padding:0!important;' +
              'flex:none!important;border:0!important;box-shadow:none!important;accent-color:#ae0f0a!important">' +
              '<span>Recordarme en este equipo</span></label>';
            var tilde = celda.querySelector('input');
            tilde.checked = !!guardado;
            entrar.form.addEventListener('submit', function () {
              if (tilde.checked && usuario.value && clave.value)
                window.parent.postMessage({ plataCuenta: { guardar: { usuario: usuario.value, clave: clave.value } } }, '*');
              else if (!tilde.checked && guardado)
                window.parent.postMessage({ plataCuenta: 'olvidar' }, '*');
            });
            if (!guardado) return;
            var fila = tabla.insertRow(0), arriba = fila.insertCell(0);
            var boton = document.createElement('input');
            boton.type = 'button';
            boton.className = entrar.className;
            boton.value = 'Iniciar sesión como ' + guardado;
            boton.style.cssText = 'margin:0!important';
            boton.addEventListener('click', function () { window.parent.postMessage({ plataCuenta: 'clave' }, '*'); });
            var otra = document.createElement('a');
            otra.href = '#';
            otra.textContent = 'Usar otra cuenta';
            otra.style.cssText = 'display:block!important;margin:8px 0 18px!important;font-size:12.5px!important;' +
              'text-align:center!important;color:#5f6770!important;text-decoration:underline!important';
            otra.addEventListener('click', function (e) {
              e.preventDefault();
              fila.remove();
              tilde.checked = false;
              usuario.focus();
            });
            arriba.appendChild(boton);
            arriba.appendChild(otra);
          }
        })();
        """;

    private const int MaxPasos = 5;
    private readonly Portero _portero;

    public Lanzador(Portero portero) => _portero = portero;

    /// <summary>La dirección del lanzador dentro del enlace, o null si no es válida o no está permitida.</summary>
    public Uri? Direccion(string enlace)
    {
        if (!enlace.StartsWith(Esquema + ":", StringComparison.OrdinalIgnoreCase)) return null;
        if (!Uri.TryCreate(enlace[(Esquema.Length + 1)..], UriKind.Absolute, out var url) || !_portero.Permitido(url)) return null;
        // Igual que el visor oficial (versión 3 del protocolo).
        return new Uri(url.AbsoluteUri + (url.Query.Length > 0 ? "&" : "?") + "v=3");
    }

    public async Task<Lanzamiento> LanzarAsync(string enlace)
    {
        var url = Direccion(enlace) ?? throw new InvalidOperationException("enlace no válido o fuera de los dominios permitidos");
        for (var paso = 1; paso <= MaxPasos; paso++)
        {
            var enviadas = _portero.NombresDeCookiesPara(url);
            var (estado, html, final) = await _portero.TraerPaginaAsync(url);
            Console.WriteLine($"lanzador paso {paso}: {final.GetLeftPart(UriPartial.Path)} -> {estado}, {html.Length} caracteres; cookies enviadas: {enviadas}");
            var pagina = LeerPagina(html, final);
            if (pagina.Xap is { } xap)
                return new Lanzamiento(xap, final, pagina.InitParams ?? "", _portero.Cookies(), Titulo: pagina.Titulo);
            if (pagina.Siguiente is null)
                throw new InvalidOperationException("la página no abrió la aplicación: " + TextoVisible(html));
            if (!_portero.Permitido(pagina.Siguiente))
                throw new InvalidOperationException("redirección fuera de los dominios permitidos: " + pagina.Siguiente.Host);
            url = pagina.Siguiente;
        }
        throw new InvalidOperationException("demasiadas redirecciones");
    }

    internal sealed record Pagina(Uri? Xap, string? InitParams, Uri? Siguiente, string? Titulo);

    /// <summary>
    /// Lee una página del lanzador: o trae el objeto Silverlight (como &lt;object&gt; o, con ASP.NET AJAX,
    /// como HTML dentro de un string de JavaScript), o redirige con <c>window.location = '...'</c>.
    /// </summary>
    internal static Pagina LeerPagina(string html, Uri url)
    {
        var js = DesescaparJs(html);
        var source = Param(js, "source") ?? (Regex.Match(js, "\"source\"\\s*:\\s*\"([^\"]+)\"") is { Success: true } m
            ? WebUtility.HtmlDecode(m.Groups[1].Value.Replace("\\/", "/")) : null);
        var titulo = Regex.Match(html, "<title[^>]*>([^<]*)</title>", RegexOptions.IgnoreCase) is { Success: true } t
            ? WebUtility.HtmlDecode(t.Groups[1].Value).Trim() : null;
        if (source is not null && Uri.TryCreate(url, source, out var xap))
            return new Pagina(xap, Param(js, "initParams"), null, titulo);
        var r = Regex.Match(html, "window\\.location(?:\\.href)?\\s*=\\s*['\"]([^'\"]+)['\"]");
        return new Pagina(null, null, r.Success && Uri.TryCreate(url, WebUtility.HtmlDecode(r.Groups[1].Value), out var sig) ? sig : null, titulo);
    }

    private static string DesescaparJs(string s)
        => Regex.Replace(s, "\\\\u([0-9a-fA-F]{4})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString())
            .Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\'", "'").Replace("\\\"", "\"");

    private static string? Param(string html, string nombre)
    {
        var n = Regex.Escape(nombre);
        var m = Regex.Match(html, $"<param[^>]*name=[\"']{n}[\"'][^>]*value=[\"']([^\"']*)[\"']", RegexOptions.IgnoreCase);
        if (!m.Success) m = Regex.Match(html, $"<param[^>]*value=[\"']([^\"']*)[\"'][^>]*name=[\"']{n}[\"']", RegexOptions.IgnoreCase);
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
    }

    // Para el mensaje de error: el texto de la página sin etiquetas ni scripts, corto.
    private static string TextoVisible(string html)
    {
        var t = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        t = WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(t, "<[^>]+>", " "), "\\s+", " ")).Trim();
        return t.Length > 160 ? t[..160] + "…" : t;
    }
}
