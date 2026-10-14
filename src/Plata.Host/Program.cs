// SPDX-License-Identifier: MIT
using Photino.NET;
using Plata.Host;

var motor = ArgOf(args, "--motor") ?? Path.Combine(AppContext.BaseDirectory, "motor");
var servidor = new ServidorDeArchivos(motor);

var win = new PhotinoWindow()
    .SetTitle("Plata")
    .SetUseOsDefaultSize(true)
    .SetLogVerbosity(0)
    .RegisterCustomSchemeHandler("plata", (object _, string _, string url, out string ct) =>
    {
        var s = servidor.Resolver(url, out ct);
        return s ?? Stream.Null;
    })
    .RegisterWebMessageReceivedHandler((_, m) =>
    {
        if (m == "plata:listo") Console.WriteLine("motor listo");
    })
    .Load(new Uri("plata://app/"));

win.WaitForClose();

static string? ArgOf(string[] a, string n)
{
    var i = Array.IndexOf(a, n);
    return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
}
