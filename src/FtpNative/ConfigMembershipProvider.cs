using System.IO;
using System.Security.Claims;
using FubarDev.FtpServer.AccountManagement;

namespace FtpNative;

internal sealed class ConfigMembershipProvider : IMembershipProviderAsync
{
    private readonly ServerConfig _config;

    public ConfigMembershipProvider(ServerConfig config)
    {
        _config = config;
    }

    public Task<MemberValidationResult> ValidateUserAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var account = _config.FindUser(username);
        if (account is null)
        {
            FtpEvents.Add($"Login recusado (usuario inexistente): {username}");
            return Task.FromResult(new MemberValidationResult(MemberValidationStatus.InvalidLogin));
        }

        if (!PasswordHasher.Verify(password, account.PasswordHash))
        {
            FtpEvents.Add($"Login recusado (senha incorreta): {username}");
            return Task.FromResult(new MemberValidationResult(MemberValidationStatus.InvalidLogin));
        }

        if (_config.UserIsolationPerFolder)
        {
            Directory.CreateDirectory(Path.Combine(_config.RootPath, "users", account.Username));
        }

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimsIdentity.DefaultNameClaimType, account.Username),
                new Claim(ClaimsIdentity.DefaultRoleClaimType, "users"),
                new Claim(ClaimTypes.AuthenticationMethod, "FtpNative"),
            },
            "FtpNative");

        FtpEvents.Add($"Login OK: {account.Username}");
        return Task.FromResult(new MemberValidationResult(MemberValidationStatus.AuthenticatedUser, new ClaimsPrincipal(identity)));
    }

    public Task<MemberValidationResult> ValidateUserAsync(string username, string password) =>
        ValidateUserAsync(username, password, CancellationToken.None);

    public Task LogOutAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public static class FtpEvents
{
    private static readonly object Sync = new();
    private static readonly List<string> History = new();

    public static event Action<string>? LineAdded;

    public static void Add(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss} {message}";
        lock (Sync)
        {
            History.Add(line);
            if (History.Count > 500)
            {
                History.RemoveRange(0, History.Count - 400);
            }
        }

        try
        {
            File.AppendAllText(ServerConfigStore.LogPath, $"{DateTime.Now:yyyy-MM-dd} {line}{Environment.NewLine}");
        }
        catch
        {
        }

        LineAdded?.Invoke(line);
    }

    public static IReadOnlyList<string> Snapshot()
    {
        lock (Sync)
        {
            return History.ToArray();
        }
    }
}
