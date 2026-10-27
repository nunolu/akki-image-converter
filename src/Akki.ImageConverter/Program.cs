using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
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
        Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }
}
