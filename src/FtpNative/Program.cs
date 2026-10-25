using System.Runtime.InteropServices;
using System.Windows;
using FtpNative;

namespace FtpNative;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var first = args.Length > 0 ? args[0].TrimStart('-', '/').ToLowerInvariant() : "";

        switch (first)
        {
            case "service":
                AttachParentConsole();
                return FtpRuntime.RunHost(args, asService: true);
            case "run":
            case "console":
                AttachParentConsole();
                return FtpRuntime.RunHost(args, asService: false);
            case "":
                return RunGui();
            case "install":
            case "uninstall":
            case "start":
            case "stop":
            case "restart":
            case "status":
            case "add-user":
            case "set-password":
            case "remove-user":
            case "list-users":
            case "open-firewall":
            case "help":
            case "h":
            case "?":
            case "version":
                AttachParentConsole();
                return InstallerCli.Dispatch(args);
            default:
                AttachParentConsole();
                ConsoleLog.Write($"Comando desconhecido: {args[0]}");
                return InstallerCli.Dispatch(new[] { "help" });
        }
    }

    private static int RunGui()
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var window = new FtpNative.Ui.MainWindow();
            app.Run(window);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "FtpNative - erro", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private static void AttachParentConsole()
    {
        try
        {
            AttachConsole(-1);
        }
        catch
        {
        }

        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch
        {
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);
}
