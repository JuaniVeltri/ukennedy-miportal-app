// SPDX-License-Identifier: MIT
using System.Windows.Controls;

namespace Plata.MotorApp;

public partial class PantallaDePrueba : UserControl
{
    public PantallaDePrueba()
    {
        InitializeComponent();
        Loaded += (_, _) => OpenSilver.Interop.ExecuteJavaScriptVoid(
            "if (window.external && window.external.sendMessage) window.external.sendMessage('plata:listo');");
    }
}
