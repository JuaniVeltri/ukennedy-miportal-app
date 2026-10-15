// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.JSInterop;

namespace Plata.MotorWasm
{
    /// <summary>
    /// Herramientas de diagnóstico para desarrollo: se llaman desde la consola o un arnés con
    /// <c>DotNet.invokeMethodAsync('Plata.MotorWasm', 'PlataArbol', profundidad)</c>.
    /// </summary>
    public static class Diagnostico
    {
        // El informe usa el interop de OpenSilver: solo con la app creada, y sin reentrar
        // (si informar falla, no hay que volver a informar).
        internal static bool AppCreada;
        private static bool _informando;

        /// <summary>
        /// Informa cada excepción (también las que la app atrapa y no muestra) con el lugar donde
        /// ocurre: las apps suelen relanzar con "throw ex" y pierden el stack original.
        /// </summary>
        internal static void InformarExcepciones(Action<string> informar)
        {
            AppDomain.CurrentDomain.FirstChanceException += (_, ev) =>
            {
                if (!AppCreada || _informando) return;
                _informando = true;
                try
                {
                    var tipo = ev.Exception is PlatformNotSupportedException ? "no-soportado" : "excepcion";
                    informar($"plata:{tipo} {ev.Exception.GetType().Name}: {ev.Exception.Message} @ " +
                        string.Join(" | ", Environment.StackTrace.Split('\n').Select(l => l.Trim())
                            .Where(l => l.StartsWith("at ") && !l.Contains("Plata.MotorWasm") && !l.Contains("System.Environment"))
                            .Take(30)));
                }
                catch { }
                finally { _informando = false; }
            };
        }

        /// <summary>El árbol visual: tipo, nombre, tamaño, visibilidad, opacidad y DataContext.</summary>
        [JSInvokable("PlataArbol")]
        public static string Arbol(int profundidad)
        {
            var sb = new StringBuilder();
            Recorrer(Application.Current?.RootVisual, 0, Math.Max(1, profundidad), sb);
            return sb.ToString();
        }

        private static void Recorrer(DependencyObject? d, int nivel, int maximo, StringBuilder sb)
        {
            if (d is null || nivel > maximo) return;
            sb.Append(' ', nivel * 2).Append(d.GetType().Name);
            if (d is FrameworkElement fe)
            {
                if (!string.IsNullOrEmpty(fe.Name)) sb.Append(" '").Append(fe.Name).Append('\'');
                sb.Append($" {fe.ActualWidth:0}x{fe.ActualHeight:0}");
                if (fe.Visibility != Visibility.Visible) sb.Append(" OCULTO");
                if (fe.Opacity < 1) sb.Append($" op={fe.Opacity:0.##}");
                if (fe.DataContext is { } dc && (fe.Parent as FrameworkElement)?.DataContext != dc)
                    sb.Append(" dc=").Append(dc.GetType().Name);
            }
            if (d is TextBlock tb && !string.IsNullOrEmpty(tb.Text))
                sb.Append(" \"").Append(tb.Text.Length > 30 ? tb.Text[..30] + "…" : tb.Text).Append('"');
            sb.Append('\n');
            var n = VisualTreeHelper.GetChildrenCount(d);
            for (var i = 0; i < n; i++) Recorrer(VisualTreeHelper.GetChild(d, i), nivel + 1, maximo, sb);
        }
    }
}
