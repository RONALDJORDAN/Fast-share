using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using FubarDev.FtpServer;
using FubarDev.FtpServer.AccountManagement;
using FubarDev.FtpServer.FileSystem.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FtpNative;

internal static class FtpRuntime
{
    public static IHost BuildHost(ServerConfig config, bool windowsService)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        if (windowsService)
        {
            builder.Services.AddWindowsService(options => options.ServiceName = InstallerCli.ServiceName);
        }

        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton<IMembershipProvider, ConfigMembershipProvider>();

        if (config.AllowAnonymous)
        {
            builder.Services.AddSingleton<IMembershipProvider, AnonymousMembershipProvider>();
        }

        builder.Services.AddFtpServer(ftp =>
        {
            ftp.UseDotNetFileSystem();
            if (config.UserIsolationPerFolder)
            {
                ftp.UseRootPerUser(options =>
                {
                    options.UserRootDirectory = "users";
                    options.AnonymousRootDirectory = "anonymous";
                });
            }
            else
            {
                ftp.UseSingleRoot();
            }
        });

        builder.Services.Configure<DotNetFileSystemOptions>(options =>
        {
            options.RootPath = config.RootPath;
            options.AllowNonEmptyDirectoryDelete = true;
        });

        builder.Services.Configure<FtpServerOptions>(options =>
        {
            options.ServerAddress = string.IsNullOrWhiteSpace(config.ServerAddress) ? null : config.ServerAddress;
            options.Port = config.Port;
        });

        builder.Services.Configure<SimplePasvOptions>(options =>
        {
            options.PasvMinPort = config.PasvMinPort;
            options.PasvMaxPort = config.PasvMaxPort;
            options.PublicAddress = string.IsNullOrWhiteSpace(config.PublicAddress)
                ? null
                : IPAddress.Parse(config.PublicAddress);
        });

        builder.Services.AddHostedService<FtpServerWorker>();
        builder.Services.AddHostedService<DashboardWorker>();

        return builder.Build();
    }

    public static int RunHost(string[] args, bool asService)
    {
        var configPath = ServerConfigStore.DefaultConfigPath;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase))
            {
                configPath = args[i + 1];
            }
        }

        var config = ServerConfigStore.LoadOrCreate(configPath, out var created, out var generated);
        if (created && generated is not null)
        {
            ConsoleLog.Write($"Senha do usuario admin gerada: {generated}");
        }

        FtpEvents.Add(asService ? "Iniciando servico do Windows" : "Iniciando modo console");
        var host = BuildHost(config, asService);
        ConsoleLog.Write($"FTP na porta {config.Port} | raiz: {config.RootPath} | painel web: porta {config.WebPort}");
        host.Run();
        FtpEvents.Add("Servidor encerrado");
        return 0;
    }

    public static string? PrimaryLanIp()
    {
        var candidates = LanAddresses();
        foreach (var prefix in new[] { "192.168.", "10.", "172." })
        {
            var match = candidates.FirstOrDefault(ip => ip.StartsWith(prefix, StringComparison.Ordinal));
            if (match is not null)
            {
                return match;
            }
        }

        return candidates.FirstOrDefault();
    }

    public static List<string> LanAddresses()
    {
        var result = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel ||
                    nic.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(address.Address))
                    {
                        var text = address.Address.ToString();
                        if (!result.Contains(text))
                        {
                            result.Add(text);
                        }
                    }
                }
            }
        }
        catch
        {
        }

        if (result.Count == 0)
        {
            result.Add("127.0.0.1");
        }

        return result;
    }

    public static bool IsPortFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class FtpServerWorker : IHostedService
{
    private readonly IFtpServerHost _ftpServerHost;
    private readonly ServerConfig _config;

    public FtpServerWorker(IFtpServerHost ftpServerHost, ServerConfig config)
    {
        _ftpServerHost = ftpServerHost;
        _config = config;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ServerConfigStore.EnsureDirectories(_config);
        try
        {
            await _ftpServerHost.StartAsync(cancellationToken);
            FtpEvents.Add($"Servidor FTP escutando na porta {_config.Port} (raiz: {_config.RootPath})");
        }
        catch (Exception ex)
        {
            FtpEvents.Add($"Falha ao iniciar FTP: {ex.Message}");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _ftpServerHost.StopAsync(cancellationToken);
        FtpEvents.Add("Servidor FTP parado");
    }
}

internal sealed class DashboardWorker : IHostedService
{
    private readonly ServerConfig _config;
    private WebDashboard? _dashboard;

    public DashboardWorker(ServerConfig config)
    {
        _config = config;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_config.WebPort > 0)
        {
            _dashboard = new WebDashboard(_config);
            _dashboard.Start();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _dashboard?.Dispose();
        _dashboard = null;
        return Task.CompletedTask;
    }
}

internal static class ConsoleLog
{
    public static void Write(string message)
    {
        try
        {
            Console.WriteLine(message);
        }
        catch
        {
        }

        try
        {
            File.AppendAllText(ServerConfigStore.LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
