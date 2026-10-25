using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FtpNative;

public sealed class FtpUserAccount
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}

public sealed class WebAccessRequest
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime RequestedUtc { get; set; } = DateTime.UtcNow;
    public string RequestedFrom { get; set; } = "";
}

public sealed class ServerConfig
{
    public int Port { get; set; } = 21;
    public string RootPath { get; set; } = "";
    public bool AllowAnonymous { get; set; }
    public bool UserIsolationPerFolder { get; set; }
    public int PasvMinPort { get; set; } = 30000;
    public int PasvMaxPort { get; set; } = 30100;
    public int WebPort { get; set; } = 8080;
    public bool AutoStartWithWindows { get; set; }
    public string? ServerAddress { get; set; }
    public string? PublicAddress { get; set; }
    public List<FtpUserAccount> Users { get; set; } = new();
    public List<WebAccessRequest> AccessRequests { get; set; } = new();

    [JsonIgnore]
    public string ConfigPath { get; set; } = ServerConfigStore.DefaultConfigPath;

    public FtpUserAccount? FindUser(string username) =>
        Users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public FtpUserAccount? PrimaryUser => Users.FirstOrDefault();
}

public static class ServerConfigStore
{
    private static readonly object SaveSync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FtpNative");

    public static string DefaultConfigPath => Path.Combine(DataDirectory, "config.json");
    public static string DefaultRootPath => Path.Combine(DataDirectory, "root");
    public static string LogPath => Path.Combine(DataDirectory, "log.txt");

    public static ServerConfig LoadOrCreate(
        string path,
        string? username,
        string? password,
        out bool created,
        out string? generatedPassword)
    {
        generatedPassword = null;
        created = !File.Exists(path);
        var config = created ? CreateDefault() : Load(path);
        config.ConfigPath = path;
        var accountAdded = false;

        if (!string.IsNullOrWhiteSpace(username))
        {
            var account = config.FindUser(username);
            if (account is null)
            {
                var pwd = string.IsNullOrEmpty(password) ? PasswordHasher.NewPassword() : password;
                config.Users.Add(new FtpUserAccount
                {
                    Username = username,
                    Password = pwd,
                    PasswordHash = PasswordHasher.Hash(pwd),
                });

                if (string.IsNullOrEmpty(password))
                {
                    generatedPassword = pwd;
                }

                accountAdded = true;
            }
            else if (!string.IsNullOrEmpty(password))
            {
                account.Password = password;
                account.PasswordHash = PasswordHasher.Hash(password);
            }
        }

        if (created || accountAdded)
        {
            Save(config, path);
        }

        return config;
    }

    public static ServerConfig LoadOrCreate(string path, out bool created, out string? generatedPassword) =>
        LoadOrCreate(path, "admin", null, out created, out generatedPassword);

    public static ServerConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            return CreateDefault();
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"Nao foi possivel ler {path}: {ex.Message}", ex);
        }

        ServerConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<ServerConfig>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Arquivo de configuracao invalido: {path}", ex);
        }

        config ??= CreateDefault();
        config.ConfigPath = path;
        Normalize(config);
        return config;
    }

    public static void Save(ServerConfig config, string path)
    {
        lock (SaveSync)
        {
            SaveCore(config, path);
        }
    }

    private static void SaveCore(ServerConfig config, string path)
    {
        Normalize(config);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    public static ServerConfig Update(string path, Func<ServerConfig, bool> mutate)
    {
        lock (SaveSync)
        {
            var config = File.Exists(path) ? Load(path) : CreateDefault();
            config.ConfigPath = path;
            if (mutate(config))
            {
                SaveCore(config, path);
            }

            return config;
        }
    }

    public static void Normalize(ServerConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.RootPath))
        {
            config.RootPath = DefaultRootPath;
        }

        if (config.Port < 1 || config.Port > 65535)
        {
            config.Port = 21;
        }

        if (config.PasvMinPort < 1024)
        {
            config.PasvMinPort = 30000;
        }

        if (config.PasvMaxPort <= config.PasvMinPort)
        {
            config.PasvMaxPort = config.PasvMinPort + 100;
        }

        if (config.WebPort < 0 || config.WebPort > 65535)
        {
            config.WebPort = 8080;
        }

        config.Users ??= new List<FtpUserAccount>();
        config.AccessRequests ??= new List<WebAccessRequest>();
    }

    public static void EnsureDirectories(ServerConfig config)
    {
        Directory.CreateDirectory(config.RootPath);
        if (config.UserIsolationPerFolder)
        {
            Directory.CreateDirectory(Path.Combine(config.RootPath, "users"));
        }

        if (config.AllowAnonymous)
        {
            Directory.CreateDirectory(Path.Combine(config.RootPath, "anonymous"));
        }
    }

    public static ServerConfig CreateDefault() => new()
    {
        Port = 21,
        RootPath = DefaultRootPath,
        UserIsolationPerFolder = false,
        WebPort = 8080,
    };
}
