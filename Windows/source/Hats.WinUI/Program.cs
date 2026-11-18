using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Hats.Core;

namespace Hats.WinUI;

internal static class Program
{
    internal static string AppVersion => typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    [STAThread]
    static void Main(string[] arguments)
    {
        if (arguments.Length > 0 && arguments[0] == "--apply-app-update")
        {
            try
            {
                if (arguments.Length != 2) throw new IOException("Invalid app update request.");
                AppUpdateInstaller.ApplyAsync(arguments[1], AppSettings.Log).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                AppSettings.Log("ERROR updating app: " + e); Environment.ExitCode = 1;
                MessageBox(IntPtr.Zero, "The app update could not finish. " + e.Message + "\n\nDetails: " + AppSettings.LogPath, "Nexus app update", 0x10);
            }
            return;
        }
        if (arguments.Length > 0 && arguments[0] == "--format-helper")
        {
            Environment.ExitCode = arguments.Length == 3 && int.TryParse(arguments[2], out var parentId)
                ? FormatElevation.RunHelperAsync(arguments[1], parentId).GetAwaiter().GetResult() : 1;
            return;
        }
        using var mutex = new Mutex(true, "Local\\NexusHATSDesktopWinUI2", out var first);
        if (!first) { MessageBox(IntPtr.Zero, "Nexus HATS Installer is already open.", "Nexus", 0x40); return; }
        try
        {
            AppSettings.Log($"Starting Nexus HATS Installer {AppVersion}.");
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(args =>
            {
                AppSettings.Log("Starting the application interface.");
                var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(queue));
                try { _ = new App(); }
                catch (Exception e) { AppSettings.Log("Application creation failed: " + e); MessageBox(IntPtr.Zero, "The interface could not start. " + e.Message + "\n\nDetails were saved to:\n" + AppSettings.LogPath, "Nexus HATS Installer", 0x10); Environment.Exit(1); }
            });
        }
        catch (Exception e) { AppSettings.Log(e.ToString()); MessageBox(IntPtr.Zero, "The app could not start. " + e.Message + "\n\nLog: " + AppSettings.LogPath, "Nexus HATS Installer", 0x10); }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBox(IntPtr window, string message, string title, uint flags);
}

public sealed partial class App : Application
{
    Window? window;
    public App()
    {
        AppSettings.Log("Creating application resources.");
        UnhandledException += (_, e) => AppSettings.Log(e.Exception.ToString());
        InitializeComponent();
        AppSettings.Log("App.xaml loaded.");
        AppSettings.Log("Nexus theme loaded.");
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppSettings.Log("Creating main window.");
        window = new MainWindow();
        window.Activate();
        AppSettings.Log($"Nexus HATS Installer {Program.AppVersion} opened.");
    }
}
