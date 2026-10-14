// SPDX-License-Identifier: MIT
using System.Windows;

namespace Plata.MotorApp;

public sealed partial class App : Application
{
    public App()
    {
        InitializeComponent();
        RootVisual = new PantallaDePrueba();
    }
}
