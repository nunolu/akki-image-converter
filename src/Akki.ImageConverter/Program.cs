using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
#if !STORE_BUILD
using Velopack;
#endif

namespace Akki.ImageConverter;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
#if !STORE_BUILD
        VelopackApp.Build().Run();
#endif
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        // Different installations and development builds must not steal each other's activations.
        var path = Environment.ProcessPath ?? AppContext.BaseDirectory;
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
        var instance = AppInstance.FindOrRegisterForKey("akki-" + key);
        if (!instance.IsCurrent && activation is not null)
        {
            // Redirect off the STA thread, which has not started a message loop yet.
            Task.Run(async () => await instance.RedirectActivationToAsync(activation)).GetAwaiter().GetResult();
            return;
        }
        instance.Activated += (_, incoming) => App.QueueActivation(incoming);
        Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(args, activation);
        });
    }
}
