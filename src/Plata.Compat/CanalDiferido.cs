// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using System.ServiceModel;

namespace Plata.Compat;

/// <summary>
/// Canal WCF que se abre recién en la primera llamada, y en forma asincrónica.
/// </summary>
/// <remarks>
/// Las apps de Silverlight crean el canal con <c>ChannelFactory&lt;T&gt;.CreateChannel()</c>, que
/// abre la fábrica de forma sincrónica. El cliente WCF de .NET implementa esa apertura
/// esperando una tarea (<c>Task.Yield</c> + espera bloqueante), y el navegador, con un solo
/// hilo, no permite bloquear. Este canal implementa la interfaz del servicio: en cada
/// <c>BeginX</c> abre fábrica y canal con <c>BeginOpen</c>/<c>EndOpen</c> y recién entonces hace
/// la llamada real; <c>EndX</c> entrega su resultado. Silverlight solo admitía el par
/// asincrónico Begin/End, así que no hay llamadas sincrónicas que atender.
/// </remarks>
public class CanalDiferido : DispatchProxy
{
    private static readonly ConcurrentDictionary<Type, Type> Interfaces = new();
    private static readonly Lazy<ModuleBuilder> Modulo = new(() =>
        AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Plata.Canales"), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("Plata.Canales"));

    private ChannelFactory _fabrica = null!;
    private Func<object> _crear = null!;
    private Task<object>? _apertura;
    private object? _canal;

    internal static T Crear<T>(ChannelFactory<T> fabrica)
    {
        // El canal también tiene que ser ICommunicationObject (las apps lo cierran así):
        // se arma una interfaz que hereda de las dos.
        var interfaz = Interfaces.GetOrAdd(typeof(T), InterfazCombinada);
        var proxy = (CanalDiferido)Create(interfaz, typeof(CanalDiferido));
        proxy._fabrica = fabrica;
        proxy._crear = () => fabrica.CreateChannel()!;
        return (T)(object)proxy;
    }

    private static Type InterfazCombinada(Type servicio)
    {
        lock (Modulo)
        {
            var tb = Modulo.Value.DefineType("Canal_" + servicio.FullName!.Replace('.', '_').Replace('+', '_'),
                TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
            tb.AddInterfaceImplementation(servicio);
            tb.AddInterfaceImplementation(typeof(ICommunicationObject));
            return tb.CreateType();
        }
    }

    protected override object? Invoke(MethodInfo? metodo, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(metodo);
        args ??= Array.Empty<object>();
        if (metodo.DeclaringType == typeof(ICommunicationObject)) return Comunicacion(metodo);

        var parametros = metodo.GetParameters();
        if (metodo.ReturnType == typeof(IAsyncResult) && parametros.Length >= 2 &&
            parametros[^2].ParameterType == typeof(AsyncCallback))
            return Comenzar(metodo, args);
        if (parametros.Length >= 1 && parametros[^1].ParameterType == typeof(IAsyncResult))
            return Terminar(metodo, args);

        throw new NotSupportedException(
            $"Plata: llamada sincrónica a '{metodo.Name}' en un servicio WCF; el navegador solo admite Begin/End.");
    }

    private IAsyncResult Comenzar(MethodInfo metodo, object?[] args)
    {
        var resultado = new ResultadoDiferido(args[^1]);
        var alTerminar = (AsyncCallback?)args[^2];
        _ = LlamarAsync();
        return resultado;

        async Task LlamarAsync()
        {
            try
            {
                var canal = await (_apertura ??= AbrirAsync());
                // El estado original pasa tal cual: hay canales (p. ej. Bpf.MessageInspectorChannel)
                // que lo leen desde adentro de la cadena de WCF.
                var reales = (object?[])args.Clone();
                reales[^2] = (AsyncCallback)(r => { resultado.Completar(real: r); alTerminar?.Invoke(resultado); });
                metodo.Invoke(canal, reales);
            }
            catch (Exception e)
            {
                resultado.Completar(error: e is TargetInvocationException { InnerException: { } i } ? i : e);
                alTerminar?.Invoke(resultado);
            }
        }
    }

    private object? Terminar(MethodInfo metodo, object?[] args)
    {
        var resultado = (ResultadoDiferido)args[^1]!;
        if (resultado.Error is not null) ExceptionDispatchInfo.Capture(resultado.Error).Throw();
        var reales = (object?[])args.Clone();
        reales[^1] = resultado.Real;
        try { return metodo.Invoke(_canal, reales); }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

    private async Task<object> AbrirAsync()
    {
        if (_fabrica.State == CommunicationState.Created)
            await Task.Factory.FromAsync(_fabrica.BeginOpen, _fabrica.EndOpen, null);
        var canal = _crear();
        var comunicacion = (ICommunicationObject)canal;
        if (comunicacion.State == CommunicationState.Created)
            await Task.Factory.FromAsync(comunicacion.BeginOpen, comunicacion.EndOpen, null);
        _canal = canal;
        return canal;
    }

    // ICommunicationObject del canal: cerrar es abortar (en HTTP no hay sesión que cerrar y
    // Close esperaría bloqueando); el resto de las operaciones sincrónicas no se admiten.
    private object? Comunicacion(MethodInfo metodo)
    {
        switch (metodo.Name)
        {
            case nameof(ICommunicationObject.Abort):
            case nameof(ICommunicationObject.Close):
                (_canal as ICommunicationObject)?.Abort();
                return null;
            case "get_State":
                return (_canal as ICommunicationObject)?.State ?? CommunicationState.Created;
            default:
                if (metodo.Name.StartsWith("add_", StringComparison.Ordinal) ||
                    metodo.Name.StartsWith("remove_", StringComparison.Ordinal))
                    return null;
                throw new NotSupportedException($"Plata: '{metodo.Name}' no está disponible en un canal diferido.");
        }
    }

    private sealed class ResultadoDiferido(object? estado) : IAsyncResult
    {
        private readonly ManualResetEvent _evento = new(false);
        public object? AsyncState { get; } = estado;
        public WaitHandle AsyncWaitHandle => _evento;
        public bool CompletedSynchronously => false;
        public bool IsCompleted { get; private set; }
        public IAsyncResult? Real { get; private set; }
        public Exception? Error { get; private set; }

        public void Completar(IAsyncResult? real = null, Exception? error = null)
        {
            Real = real;
            Error = error;
            IsCompleted = true;
            _evento.Set();
        }
    }
}
