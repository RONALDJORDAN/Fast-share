using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace FtpNative;

internal static class InstallerCli
{
    public const string ServiceName = "FtpNative";
    public const string ServiceDisplayName = "FtpNative - Servidor FTP";
    private const string ServiceDescription = "Servidor FTP nativo em .NET (FubarDev.FtpServer). Inicia automaticamente com o Windows.";
    private const string UserNamePattern = "^[A-Za-z0-9._-]{1,64}$";

    public static string DefaultInstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FtpNative");

    public static int Dispatch(string[] args)
    {
        var rest = args.ToList();
        var configPath = TakeValue(rest, "--config") ?? ServerConfigStore.DefaultConfigPath;

        if (rest.Count == 0)
        {
            ShowHelp();
            return 0;
        }

        var command = rest[0].TrimStart('-', '/').ToLowerInvariant();
        rest.RemoveAt(0);

        try
        {
            return command switch
            {
                "install" => Install(rest, configPath),
                "uninstall" => Uninstall(configPath),
                "start" => StartService(),
                "stop" => StopService(),
                "restart" => RestartService(configPath),
                "status" => ShowStatus(configPath),
                "add-user" => AddUser(rest, configPath),
                "set-password" => SetPassword(rest, configPath),
                "remove-user" => RemoveUser(rest, configPath),
                "list-users" => ListUsers(configPath),
                "open-firewall" => OpenFirewall(configPath),
                "help" or "h" or "?" => ShowHelp(),
                "version" => ShowVersion(),
                _ => Unknown(command),
            };
        }
        catch (ServiceNotFoundException ex)
        {
            ConsoleLog.Write(ex.Message);
            return 1;
        }
        catch (Win32Exception ex)
        {
            ConsoleLog.Write($"Erro do Windows: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            ConsoleLog.Write($"Erro: {ex.Message}");
            return 1;
        }
    }

    private static int Install(List<string> args, string configPath)
    {
        if (!IsAdmin())
        {
            ConsoleLog.Write("Esta operacao requer privilegios de administrador.");
            return 3;
        }

        var installDir = TakeValue(args, "--dir") ?? DefaultInstallDir;
        var adminUser = TakeValue(args, "--admin-user") ?? "admin";
        var adminPassword = TakeValue(args, "--admin-password");
        var rootOverride = TakeValue(args, "--root");
        var port = TakeInt(args, "--port");
        var webPort = TakeInt(args, "--webport");
        var pasvMin = TakeInt(args, "--pasv-min");
        var pasvMax = TakeInt(args, "--pasv-max");
        var anonymous = HasFlag(args, "--anonymous");
        var noStart = HasFlag(args, "--no-start");

        var config = ServerConfigStore.LoadOrCreate(configPath, adminUser, adminPassword, out var created, out var generated);
        if (generated is not null)
        {
            ConsoleLog.Write($"Senha gerada para '{adminUser}': {generated}");
        }

        if (port is not null)
        {
            config.Port = port.Value;
        }

        if (webPort is not null)
        {
            config.WebPort = webPort.Value;
        }

        if (rootOverride is not null)
        {
            config.RootPath = rootOverride;
        }

        if (pasvMin is not null)
        {
            config.PasvMinPort = pasvMin.Value;
        }

        if (pasvMax is not null)
        {
            config.PasvMaxPort = pasvMax.Value;
        }

        if (anonymous)
        {
            config.AllowAnonymous = true;
        }

        config.UserIsolationPerFolder = false;
        ServerConfigStore.Normalize(config);
        ServerConfigStore.Save(config, configPath);
        ServerConfigStore.EnsureDirectories(config);

        if (args.Count > 0)
        {
            ConsoleLog.Write("Parametros ignorados: " + string.Join(" ", args));
        }

        var exeSource = Environment.ProcessPath ?? throw new InvalidOperationException("Executavel nao localizado.");
        var targetExe = Path.Combine(installDir, Path.GetFileName(exeSource));
        Directory.CreateDirectory(installDir);

        var wasRunning = TryIsRunning();
        if (wasRunning)
        {
            ConsoleLog.Write("Parando servico existente...");
            WindowsServiceControl.Stop(ServiceName);
        }

        if (!PathsEqual(exeSource, targetExe))
        {
            if (IsSingleFileBuild())
            {
                ConsoleLog.Write($"Copiando aplicacao para {targetExe}...");
                File.Copy(exeSource, targetExe, overwrite: true);
            }
            else
            {
                ConsoleLog.Write($"Modo portatil: copiando arquivos para {installDir}...");
                CopyDirectory(AppContext.BaseDirectory, installDir);
            }
        }

        var binaryPath = $"\"{targetExe}\" --service";
        if (!PathsEqual(configPath, ServerConfigStore.DefaultConfigPath))
        {
            binaryPath += $" --config \"{configPath}\"";
        }

        WindowsServiceControl.CreateOrUpdate(ServiceName, ServiceDisplayName, ServiceDescription, binaryPath);
        FirewallInstaller.InstallRules(config);

        if (!noStart)
        {
            ConsoleLog.Write("Iniciando servico...");
            WindowsServiceControl.Start(ServiceName);
        }

        var ip = FtpRuntime.PrimaryLanIp() ?? "127.0.0.1";
        ConsoleLog.Write("Instalacao concluida.");
        ConsoleLog.Write($"Servico: {ServiceDisplayName} ({(noStart ? "parado" : "em execucao")})");
        ConsoleLog.Write($"Executavel: {targetExe}");
        ConsoleLog.Write($"Configuracao: {configPath}");
        ConsoleLog.Write($"Pasta compartilhada: {config.RootPath}");
        ConsoleLog.Write($"FTP: ftp://{ip}:{config.Port}/  usuario: {adminUser}{(generated is not null ? $"  senha: {generated}" : "")}");

        if (config.WebPort > 0)
        {
            ConsoleLog.Write($"Painel web: http://{ip}:{config.WebPort}/");
        }

        return 0;
    }

    private static int Uninstall(string configPath)
    {
        if (!IsAdmin())
        {
            ConsoleLog.Write("Esta operacao requer privilegios de administrador.");
            return 3;
        }

        WindowsServiceControl.Remove(ServiceName);
        FirewallInstaller.RemoveRules();
        ConsoleLog.Write("Servico removido.");
        ConsoleLog.Write($"Arquivos e configuracoes mantidos em: {configPath}");
        return 0;
    }

    private static int StartService()
    {
        if (!IsAdmin())
        {
            ConsoleLog.Write("Esta operacao requer privilegios de administrador.");
            return 3;
        }

        WindowsServiceControl.Start(ServiceName);
        ConsoleLog.Write("Servico iniciado.");
        return 0;
    }

    private static int StopService()
    {
        if (!IsAdmin())
        {
            ConsoleLog.Write("Esta operacao requer privilegios de administrador.");
            return 3;
        }

        WindowsServiceControl.Stop(ServiceName);
        ConsoleLog.Write("Servico parado.");
        return 0;
    }

    private static int RestartService(string configPath)
    {
        if (!IsAdmin())
        {
            ConsoleLog.Write("Esta operacao requer privilegios de administrador.");
            return 3;
        }

        if (!WindowsServiceControl.TryGetState(ServiceName, out _))
        {
            ConsoleLog.Write("Servico nao instalado.");
            return 1;
        }

        FirewallInstaller.InstallRules(ServerConfigStore.Load(configPath));
        WindowsServiceControl.Stop(ServiceName);
        WindowsServiceControl.Start(ServiceName);
        ConsoleLog.Write("Servico reiniciado.");
        return 0;
    }

    private static int ShowStatus(string configPath)
    {
        var stateText = "Nao instalado";
        if (WindowsServiceControl.TryGetState(ServiceName, out var state))
        {
            stateText = StateText(state);
        }

        var config = ServerConfigStore.Load(configPath);
        ConsoleLog.Write($"Servico: {stateText}");
        ConsoleLog.Write($"Configuracao: {configPath}");
        ConsoleLog.Write($"Porta FTP: {config.Port}");
        ConsoleLog.Write($"Painel web: {(config.WebPort > 0 ? $"porta {config.WebPort}" : "desativado")}");
        ConsoleLog.Write($"Pasta compartilhada: {config.RootPath}");
        ConsoleLog.Write($"Usuarios: {(config.Users.Count == 0 ? "nenhum" : string.Join(", ", config.Users.Select(u => u.Username)))}");
        ConsoleLog.Write($"Anonimo: {(config.AllowAnonymous ? "sim" : "nao")}");
        return 0;
    }

    private static int AddUser(List<string> args, string configPath)
    {
        if (args.Count == 0)
        {
            ConsoleLog.Write("Uso: FtpNative --add-user NOME [SENHA]");
            return 2;
        }

        var username = args[0];
        if (!Regex.IsMatch(username, UserNamePattern))
        {
            ConsoleLog.Write("Nome de usuario invalido. Use letras, numeros, ponto, hifen ou sublinhado.");
            return 2;
        }

        var password = args.Count > 1 ? args[1] : null;
        var generated = password is null;
        password ??= PasswordHasher.NewPassword();

        var config = ServerConfigStore.Load(configPath);
        if (config.FindUser(username) is not null)
        {
            ConsoleLog.Write("Usuario ja existe. Use --set-password para alterar a senha.");
            return 1;
        }

        config.Users.Add(new FtpUserAccount
        {
            Username = username,
            Password = password,
            PasswordHash = PasswordHasher.Hash(password),
        });
        ServerConfigStore.Save(config, configPath);
        RestartIfRunning();
        ConsoleLog.Write($"Usuario '{username}' criado.{(generated ? $" Senha: {password}" : "")}");
        return 0;
    }

    private static int SetPassword(List<string> args, string configPath)
    {
        if (args.Count == 0)
        {
            ConsoleLog.Write("Uso: FtpNative --set-password NOME [SENHA]");
            return 2;
        }

        var username = args[0];
        var password = args.Count > 1 ? args[1] : null;
        var generated = password is null;
        password ??= PasswordHasher.NewPassword();

        var config = ServerConfigStore.Load(configPath);
        var user = config.FindUser(username);
        if (user is null)
        {
            ConsoleLog.Write("Usuario nao encontrado.");
            return 1;
        }

        user.Password = password;
        user.PasswordHash = PasswordHasher.Hash(password);
        ServerConfigStore.Save(config, configPath);
        RestartIfRunning();
        ConsoleLog.Write($"Senha de '{user.Username}' alterada.{(generated ? $" Senha: {password}" : "")}");
        return 0;
    }

    private static int RemoveUser(List<string> args, string configPath)
    {
        if (args.Count == 0)
        {
            ConsoleLog.Write("Uso: FtpNative --remove-user NOME");
            return 2;
        }

        var config = ServerConfigStore.Load(configPath);
        var user = config.FindUser(args[0]);
        if (user is null)
        {
            ConsoleLog.Write("Usuario nao encontrado.");
            return 1;
        }

        config.Users.Remove(user);
        ServerConfigStore.Save(config, configPath);
        RestartIfRunning();
        ConsoleLog.Write($"Usuario '{user.Username}' removido.");
        return 0;
    }

    private static int ListUsers(string configPath)
    {
        var config = ServerConfigStore.Load(configPath);
        if (config.Users.Count == 0)
        {
            ConsoleLog.Write("Nenhum usuario cadastrado.");
            return 0;
        }

        foreach (var user in config.Users)
        {
            ConsoleLog.Write($"- {user.Username}");
        }

        return 0;
    }

    private static int OpenFirewall(string configPath)
    {
        if (!IsAdmin())
        {
            ConsoleLog.Write("Esta operacao requer privilegios de administrador.");
            return 3;
        }

        var config = ServerConfigStore.Load(configPath);
        FirewallInstaller.InstallRules(config);
        ConsoleLog.Write("Regras de firewall aplicadas.");
        return 0;
    }

    private static void RestartIfRunning()
    {
        try
        {
            if (!WindowsServiceControl.TryGetState(ServiceName, out var state) || state != ServiceState.Running)
            {
                return;
            }

            WindowsServiceControl.Stop(ServiceName);
            WindowsServiceControl.Start(ServiceName);
            ConsoleLog.Write("Servico reiniciado para aplicar as alteracoes.");
        }
        catch (Exception ex)
        {
            ConsoleLog.Write("Aviso: nao foi possivel reiniciar o servico automaticamente: " + ex.Message);
        }
    }

    private static bool TryIsRunning()
    {
        try
        {
            return WindowsServiceControl.TryGetState(ServiceName, out var state) &&
                   state is ServiceState.Running or ServiceState.StartPending;
        }
        catch
        {
            return false;
        }
    }

    private static string StateText(ServiceState state) => state switch
    {
        ServiceState.Running => "Em execucao",
        ServiceState.Stopped => "Parado",
        ServiceState.StartPending => "Iniciando",
        ServiceState.StopPending => "Parando",
        ServiceState.Paused => "Pausado",
        ServiceState.PausePending => "Pausando",
        ServiceState.ContinuePending => "Continuando",
        _ => "Desconhecido",
    };

    public static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static string? TakeValue(List<string> args, params string[] names)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (names.Any(n => string.Equals(n, args[i], StringComparison.OrdinalIgnoreCase)))
            {
                var value = args[i + 1];
                args.RemoveRange(i, 2);
                return value;
            }
        }

        return null;
    }

    public static bool HasFlag(List<string> args, params string[] names)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (names.Any(n => string.Equals(n, args[i], StringComparison.OrdinalIgnoreCase)))
            {
                args.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    private static int? TakeInt(List<string> args, string name)
    {
        var value = TakeValue(args, name);
        if (value is null)
        {
            return null;
        }

        if (!int.TryParse(value, out var number))
        {
            throw new InvalidOperationException($"Valor invalido para {name}: {value}");
        }

        return number;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsSingleFileBuild() =>
        !File.Exists(Path.Combine(AppContext.BaseDirectory, "FtpNative.deps.json"));

    private static void CopyDirectory(string source, string target)
    {
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
        }
    }

    private static int Unknown(string command)
    {
        ConsoleLog.Write($"Comando desconhecido: {command}");
        ShowHelp();
        return 2;
    }

    private static int ShowVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        ConsoleLog.Write($"FtpNative {version}");
        return 0;
    }

    private static int ShowHelp()
    {
        ConsoleLog.Write("FtpNative - Servidor FTP para Windows");
        ConsoleLog.Write("");
        ConsoleLog.Write("Uso: FtpNative [comando] [opcoes]");
        ConsoleLog.Write("");
        ConsoleLog.Write("Comandos:");
        ConsoleLog.Write("  --install                 Instala como servico do Windows (inicia com o PC)");
        ConsoleLog.Write("      --port N              Porta FTP (padrao: 21)");
        ConsoleLog.Write("      --webport N           Porta do painel web (padrao: 8080, 0 desativa)");
        ConsoleLog.Write("      --root PASTA          Pasta compartilhada");
        ConsoleLog.Write("      --dir PASTA           Pasta de instalacao (padrao: Program Files\\FtpNative)");
        ConsoleLog.Write("      --admin-user NOME     Usuario principal (padrao: admin)");
        ConsoleLog.Write("      --admin-password SENHA");
        ConsoleLog.Write("      --anonymous           Permite login anonimo");
        ConsoleLog.Write("      --pasv-min / --pasv-max  Faixa de portas passivas");
        ConsoleLog.Write("      --no-start            Instala sem iniciar o servico");
        ConsoleLog.Write("  --uninstall               Remove o servico (mantem arquivos)");
        ConsoleLog.Write("  --start | --stop | --restart | --status");
        ConsoleLog.Write("  --add-user NOME [SENHA]   Cria usuario (senha gerada se omitida)");
        ConsoleLog.Write("  --set-password NOME [SENHA]");
        ConsoleLog.Write("  --remove-user NOME");
        ConsoleLog.Write("  --list-users");
        ConsoleLog.Write("  --open-firewall           Libera as portas no firewall");
        ConsoleLog.Write("  --run                     Executa no console");
        ConsoleLog.Write("  --config CAMINHO          Arquivo de configuracao alternativo");
        ConsoleLog.Write("");
        ConsoleLog.Write("Sem argumentos, abre a interface grafica.");
        return 0;
    }
}
