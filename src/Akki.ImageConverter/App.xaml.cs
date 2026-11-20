using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Runtime.InteropServices;

namespace Akki.ImageConverter;

public partial class App : Application
{
    private MainWindow? _window;
    private readonly string[] _launchPaths;
    private readonly AppActivationArguments? _initialActivation;
    private static readonly object ActivationLock = new();
    private static readonly Queue<string[]> PendingActivations = new();
    private static App? _current;
    public App(string[] launchPaths, AppActivationArguments? activation) { _launchPaths = launchPaths; _initialActivation = activation; InitializeComponent(); }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        var paths = _initialActivation?.Kind == ExtendedActivationKind.File ? ActivationPaths(_initialActivation) : _launchPaths;
        _window.ImportOnLaunch(paths);
        _window.Activate();
        lock (ActivationLock) { _current = this; }
        DrainActivations();
    }

    internal static void QueueActivation(AppActivationArguments activation)
    {
        var paths = ActivationPaths(activation);
        lock (ActivationLock) { PendingActivations.Enqueue(paths); _current?._window?.DispatcherQueue.TryEnqueue(_current.DrainActivations); }
    }

    private void DrainActivations()
    {
        while (true)
        {
            string[] paths;
            lock (ActivationLock) { if (PendingActivations.Count == 0) return; paths = PendingActivations.Dequeue(); }
            _window?.ReceiveActivation(paths);
        }
    }

    private static string[] ActivationPaths(AppActivationArguments activation)
    {
        if (activation.Kind == ExtendedActivationKind.File && activation.Data is Windows.ApplicationModel.Activation.IFileActivatedEventArgs files)
            return files.Files.Select(file => file.Path).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        if (activation.Kind == ExtendedActivationKind.Launch && activation.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch && !string.IsNullOrWhiteSpace(launch.Arguments))
        {
            var argv = CommandLineToArgvW("akki.exe " + launch.Arguments, out var count);
            if (argv == 0) return [];
            try { return Enumerable.Range(1, count - 1).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "").ToArray(); }
            finally { LocalFree(argv); }
        }
        return [];
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CommandLineToArgvW(string commandLine, out int argumentCount);
    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
