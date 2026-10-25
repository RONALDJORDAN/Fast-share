using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QRCoder;

namespace FtpNative;

internal sealed class WebDashboard : IDisposable
{
    private const string SessionCookieName = "fc_sess";
    private const int MaxImages = 1500;
    private const int MaxVideos = 600;
    private const int MaxScanDepth = 15;
    private const int MaxPendingRequests = 100;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan PersistentSessionLifetime = TimeSpan.FromDays(30);
    private static readonly Lazy<string> PageTemplate = new(() => LoadEmbeddedTemplate("webui.html"));
    private static readonly Lazy<string> LoginTemplate = new(() => LoadEmbeddedTemplate("login.html"));

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jfif", ".png", ".gif", ".webp", ".bmp", ".avif", ".svg",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".webm", ".ogv", ".ogg", ".mov", ".avi", ".mkv", ".wmv", ".3gp",
    };

    private readonly ServerConfig _config;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<DateTime>> _registrationAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<DateTime>> _loginFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _scanLock = new();
    private Task? _acceptLoop;
    private RootScan? _scanCache;
    private DateTime _scanCacheUtc;
    private DateTime _sessionCleanupUtc = DateTime.UtcNow;

    public WebDashboard(ServerConfig config)
    {
        _config = config;
        _listener = new TcpListener(IPAddress.Any, config.WebPort);
    }

    public void Start()
    {
        try
        {
            _listener.Start();
            _acceptLoop = Task.Run(AcceptLoopAsync);
            FtpEvents.Add($"FastCloud web disponivel na porta {_config.WebPort} (login obrigatorio)");
        }
        catch (Exception ex)
        {
            FtpEvents.Add($"FastCloud web nao iniciou: {ex.Message}");
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch
            {
                break;
            }

            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                var request = await ReadRequestAsync(stream);
                if (request is null)
                {
                    return;
                }

                request.RemoteAddress = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "";
                var (path, query) = SplitTarget(request.RawPath);

                if (request.Method == "POST" && path == "/login")
                {
                    await LoginAsync(stream, request);
                    return;
                }

                if (request.Method == "POST" && path == "/register")
                {
                    await RegisterAsync(stream, request);
                    return;
                }

                var auth = Authenticate(request);

                if (path == "/logout" && (request.Method == "POST" || request.Method == "GET"))
                {
                    if (auth is not null)
                    {
                        _sessions.TryRemove(auth.Value.Token, out _);
                    }

                    await RedirectAsync(stream, "/login", clearCookie: auth is not null);
                    return;
                }

                if (auth is null)
                {
                    if (path == "/login" && request.Method == "GET")
                    {
                        await ServeLoginAsync(stream, 200, null, QueryValue(query, "next") ?? "/", request);
                        return;
                    }

                    if (path.StartsWith("/api/", StringComparison.Ordinal) ||
                        path == "/d" ||
                        path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        if (path.StartsWith("/api/", StringComparison.Ordinal))
                        {
                            await WriteJsonAsync(stream, 401, new { error = "login necessario" });
                        }
                        else
                        {
                            await WriteResponseAsync(stream, 401, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("login necessario"), "no-store");
                        }

                        return;
                    }

                    var target = request.RawPath.StartsWith("/login", StringComparison.Ordinal) ? "/" : request.RawPath;
                    await RedirectAsync(stream, "/login?next=" + Uri.EscapeDataString(target));
                    return;
                }

                if (path == "/login")
                {
                    await RedirectAsync(stream, "/");
                    return;
                }

                if (request.Method == "POST")
                {
                    switch (path)
                    {
                        case "/upload":
                            await UploadAsync(stream, request, query);
                            return;
                        case "/delete":
                            await DeleteAsync(stream, query);
                            return;
                        case "/mkdir":
                            await MkdirAsync(stream, query);
                            return;
                    }
                }

                await RespondAsync(stream, request, path, query, auth.Value.Session.Username);
            }
            catch
            {
            }
        }
    }

    private async Task RespondAsync(NetworkStream stream, HttpRequest request, string path, string query, string sessionUser)
    {
        if (path is "/" or "/index.html")
        {
            await WriteResponseAsync(stream, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(BuildPage(query, sessionUser, request)), "no-store", HtmlSecurityHeaders);
            return;
        }

        if (path == "/api/list")
        {
            await WriteJsonTextAsync(stream, 200, BuildListing(QueryValue(query, "p")));
            return;
        }

        if (path == "/api/gallery")
        {
            await WriteJsonTextAsync(stream, 200, BuildGallery());
            return;
        }

        if (path == "/api/stats")
        {
            await WriteJsonTextAsync(stream, 200, BuildStats());
            return;
        }

        if (path is "/qr.png" or "/page.png")
        {
            await WriteResponseAsync(stream, 200, "image/png", BuildQr(PageUrl(request)), "no-cache");
            return;
        }

        if (path == "/ftp.png")
        {
            await WriteResponseAsync(stream, 200, "image/png", BuildQr(FtpUrl(FtpHost(request))), "no-cache");
            return;
        }

        if (path == "/d")
        {
            var file = QueryValue(query, "file");
            if (file is null)
            {
                await WriteResponseAsync(stream, 400, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("arquivo nao informado"), "no-store");
                return;
            }

            var inline = QueryValue(query, "inline") is "1" or "true";
            await DownloadAsync(stream, request, file, inline);
            return;
        }

        await WriteResponseAsync(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("nao encontrado"), "no-store");
    }

    private (string Token, Session Session)? Authenticate(HttpRequest request)
    {
        var token = CookieValue(request, SessionCookieName);
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        if (!_sessions.TryGetValue(token, out var session))
        {
            return null;
        }

        if (session.ExpiresUtc <= DateTime.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return null;
        }

        if (DateTime.UtcNow - _sessionCleanupUtc > TimeSpan.FromMinutes(10))
        {
            CleanupSessions();
        }

        return (token, session);
    }

    private async Task LoginAsync(NetworkStream stream, HttpRequest request)
    {
        if (request.ContentLength <= 0 || request.ContentLength > 8192)
        {
            await ServeLoginAsync(stream, 400, "Requisicao invalida.", "/", request);
            return;
        }

        var address = ClientAddress(request);
        if (TooManyLoginFailures(address))
        {
            await Task.Delay(500);
            await ServeLoginAsync(stream, 429, "Muitas tentativas. Aguarde alguns minutos.", "/", request);
            return;
        }

        var body = await ReadBodyAsync(stream, request, 8192);
        var form = ParseForm(Encoding.UTF8.GetString(body));
        var username = form.TryGetValue("username", out var userValue) ? userValue.Trim() : "";
        var password = form.TryGetValue("password", out var passwordValue) ? passwordValue : "";
        var remember = form.TryGetValue("remember", out var rememberValue) && rememberValue is "1" or "on" or "true";
        var next = SanitizeNext(form.TryGetValue("next", out var nextValue) ? nextValue : "/");

        var account = _config.FindUser(username);
        if (account is null || !VerifyPassword(account, password))
        {
            RecordLoginFailure(address);
            await Task.Delay(650);
            FtpEvents.Add($"Login web recusado: {username} ({address})");
            await ServeLoginAsync(stream, 401, "Usuario ou senha incorretos.", next, request);
            return;
        }

        _loginFailures.TryRemove(address, out _);
        var lifetime = remember ? PersistentSessionLifetime : SessionLifetime;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _sessions[token] = new Session(account.Username, DateTime.UtcNow.Add(lifetime));
        CleanupSessions();
        FtpEvents.Add(remember ? $"Login web OK (lembrar senha): {account.Username}" : $"Login web OK: {account.Username}");

        await WriteResponseAsync(
            stream,
            302,
            "text/plain; charset=utf-8",
            Array.Empty<byte>(),
            "no-store",
            $"Location: {next}\r\nSet-Cookie: {SessionCookie(token, remember)}");
    }

    private async Task RegisterAsync(NetworkStream stream, HttpRequest request)
    {
        if (request.ContentLength <= 0 || request.ContentLength > 8192)
        {
            await WriteJsonAsync(stream, 400, new { error = "Requisicao invalida." });
            return;
        }

        var address = ClientAddress(request);
        if (!AllowRegistration(address))
        {
            await WriteJsonAsync(stream, 429, new { error = "Muitas tentativas. Aguarde alguns minutos e tente novamente." });
            return;
        }

        var body = await ReadBodyAsync(stream, request, 8192);
        var form = ParseForm(Encoding.UTF8.GetString(body));
        var username = form.TryGetValue("username", out var userValue) ? userValue.Trim() : "";
        var password = form.TryGetValue("password", out var passwordValue) ? passwordValue : "";
        var confirm = form.TryGetValue("confirm", out var confirmValue) ? confirmValue : "";

        if (!AccountRules.IsValidUsername(username))
        {
            await WriteJsonAsync(stream, 400, new { error = "Login invalido. Use de 3 a 32 letras, numeros, ponto, hifen ou sublinhado." });
            return;
        }

        if (!AccountRules.IsValidPassword(password, out var passwordError))
        {
            await WriteJsonAsync(stream, 400, new { error = passwordError });
            return;
        }

        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            await WriteJsonAsync(stream, 400, new { error = "As senhas nao conferem." });
            return;
        }

        var error = "";
        var created = false;
        try
        {
            ServerConfigStore.Update(_config.ConfigPath, config =>
            {
                if (config.FindUser(username) is not null)
                {
                    error = "Este login ja existe. Volte e entre com sua senha.";
                    return false;
                }

                if (config.AccessRequests.Any(r => string.Equals(r.Username, username, StringComparison.OrdinalIgnoreCase)))
                {
                    error = "Ja existe uma solicitacao pendente para este login.";
                    return false;
                }

                if (config.AccessRequests.Count >= MaxPendingRequests)
                {
                    error = "Limite de solicitacoes pendentes atingido. Avise o administrador.";
                    return false;
                }

                config.AccessRequests.Add(new WebAccessRequest
                {
                    Username = username,
                    PasswordHash = PasswordHasher.Hash(password),
                    RequestedUtc = DateTime.UtcNow,
                    RequestedFrom = address,
                });
                created = true;
                return true;
            });
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(stream, 500, new { error = "Nao foi possivel registrar agora: " + ex.Message });
            return;
        }

        if (!created)
        {
            await WriteJsonAsync(stream, 409, new { error });
            return;
        }

        FtpEvents.Add($"Solicitacao de acesso web: {username} ({address})");
        await WriteJsonAsync(stream, 200, new { ok = true });
    }

    private bool AllowRegistration(string address)
    {
        var now = DateTime.UtcNow;
        var attempts = _registrationAttempts.GetOrAdd(address, _ => new List<DateTime>());
        lock (attempts)
        {
            attempts.RemoveAll(time => now - time > TimeSpan.FromHours(1));
            if (attempts.Count >= 5)
            {
                return false;
            }

            attempts.Add(now);
            return true;
        }
    }

    private static string ClientAddress(HttpRequest request)
    {
        if (request.Headers.TryGetValue("CF-Connecting-IP", out var forwarded) &&
            IPAddress.TryParse(forwarded.Trim(), out var address))
        {
            return address.ToString();
        }

        return string.IsNullOrEmpty(request.RemoteAddress) ? "desconhecido" : request.RemoteAddress;
    }

    private bool TooManyLoginFailures(string address)
    {
        var now = DateTime.UtcNow;
        var attempts = _loginFailures.GetOrAdd(address, _ => new List<DateTime>());
        lock (attempts)
        {
            attempts.RemoveAll(time => now - time > TimeSpan.FromMinutes(10));
            return attempts.Count >= 8;
        }
    }

    private void RecordLoginFailure(string address)
    {
        var attempts = _loginFailures.GetOrAdd(address, _ => new List<DateTime>());
        lock (attempts)
        {
            attempts.Add(DateTime.UtcNow);
        }
    }

    private static bool VerifyPassword(FtpUserAccount account, string password)
    {
        if (!string.IsNullOrEmpty(account.PasswordHash))
        {
            return PasswordHasher.Verify(password, account.PasswordHash);
        }

        return account.Password.Length > 0 && string.Equals(account.Password, password, StringComparison.Ordinal);
    }

    private static string SessionCookie(string token, bool remember)
    {
        var cookie = $"{SessionCookieName}={token}; Path=/; HttpOnly; SameSite=Lax";
        return remember ? cookie + $"; Max-Age={(int)PersistentSessionLifetime.TotalSeconds}" : cookie;
    }

    private void CleanupSessions()
    {
        _sessionCleanupUtc = DateTime.UtcNow;
        foreach (var pair in _sessions)
        {
            if (pair.Value.ExpiresUtc <= DateTime.UtcNow)
            {
                _sessions.TryRemove(pair.Key, out _);
            }
        }
    }

    private async Task ServeLoginAsync(NetworkStream stream, int status, string? error, string? next, HttpRequest request)
    {
        var html = BuildLogin(error, SanitizeNext(next), request);
        await WriteResponseAsync(stream, status, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html), "no-store", HtmlSecurityHeaders);
    }

    private static Task RedirectAsync(NetworkStream stream, string location, bool clearCookie = false)
    {
        var extra = "Location: " + location;
        if (clearCookie)
        {
            extra += $"\r\nSet-Cookie: {SessionCookieName}=; Path=/; HttpOnly; SameSite=Lax; Max-Age=0";
        }

        return WriteResponseAsync(stream, 302, "text/plain; charset=utf-8", Array.Empty<byte>(), "no-store", extra);
    }

    private static async Task<byte[]> ReadBodyAsync(NetworkStream stream, HttpRequest request, int max)
    {
        var length = (int)Math.Min(request.ContentLength, max);
        var body = new byte[length];
        var copied = Math.Min(request.BodyPrefix.Length, length);
        Array.Copy(request.BodyPrefix, body, copied);

        while (copied < length)
        {
            var read = await stream.ReadAsync(body.AsMemory(copied, length - copied));
            if (read <= 0)
            {
                break;
            }

            copied += read;
        }

        return copied == length ? body : body[..copied];
    }

    private static Dictionary<string, string> ParseForm(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = eq >= 0 ? pair[..eq] : pair;
            var value = eq >= 0 ? pair[(eq + 1)..] : "";
            var decodedKey = WebUtility.UrlDecode(key.Replace('+', ' ')) ?? "";
            var decodedValue = WebUtility.UrlDecode(value.Replace('+', ' ')) ?? "";
            result[decodedKey] = decodedValue;
        }

        return result;
    }

    private static string SanitizeNext(string? next)
    {
        if (string.IsNullOrWhiteSpace(next))
        {
            return "/";
        }

        var value = next.Trim();
        if (value.Length == 0 || value[0] != '/')
        {
            return "/";
        }

        if (value.StartsWith("//", StringComparison.Ordinal) ||
            value.StartsWith("/\\", StringComparison.Ordinal) ||
            value.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
        {
            return "/";
        }

        foreach (var c in value)
        {
            if (c is '\r' or '\n' or '\0')
            {
                return "/";
            }
        }

        return value;
    }

    private static string? CookieValue(HttpRequest request, string name)
    {
        if (!request.Headers.TryGetValue("Cookie", out var header))
        {
            return null;
        }

        foreach (var part in header.Split(';'))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            if (string.Equals(part[..eq].Trim(), name, StringComparison.Ordinal))
            {
                return part[(eq + 1)..].Trim();
            }
        }

        return null;
    }

    private sealed class HttpRequest
    {
        public string Method { get; init; } = "";
        public string RawPath { get; init; } = "";
        public string RemoteAddress { get; set; } = "";
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public byte[] BodyPrefix { get; set; } = Array.Empty<byte>();
        public long ContentLength { get; set; }
    }

    private sealed record Session(string Username, DateTime ExpiresUtc);

    private sealed class MediaItem
    {
        public string Name { get; init; } = "";
        public string Rel { get; init; } = "";
        public string Ext { get; init; } = "";
        public long Size { get; init; }
        public DateTime ModifiedUtc { get; init; }
        public bool Video { get; init; }
    }

    private sealed class RootScan
    {
        public List<MediaItem> Items { get; } = new();
        public int Files { get; set; }
        public int Directories { get; set; }
        public int Images { get; set; }
        public int Videos { get; set; }
        public long TotalBytes { get; set; }
        public bool Truncated { get; set; }
    }

    private static async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[32768];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total));
            if (read <= 0)
            {
                return null;
            }

            total += read;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd < 0)
            {
                continue;
            }

            var lines = text[..headerEnd].Split("\r\n");
            var parts = lines[0].Split(' ');
            if (parts.Length < 2)
            {
                return null;
            }

            var request = new HttpRequest
            {
                Method = parts[0].ToUpperInvariant(),
                RawPath = parts[1],
            };

            for (var i = 1; i < lines.Length; i++)
            {
                var colon = lines[i].IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                request.Headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
            }

            if (request.Headers.TryGetValue("Content-Length", out var lengthText) && long.TryParse(lengthText, out var length))
            {
                request.ContentLength = length;
            }

            var bodyStart = headerEnd + 4;
            var prefixLength = total - bodyStart;
            if (prefixLength > 0)
            {
                request.BodyPrefix = buffer.AsSpan(bodyStart, prefixLength).ToArray();
            }

            return request;
        }

        return null;
    }

    private static (string Path, string Query) SplitTarget(string rawPath)
    {
        var question = rawPath.IndexOf('?');
        return question >= 0
            ? (rawPath[..question], rawPath[(question + 1)..])
            : (rawPath, "");
    }

    private async Task UploadAsync(NetworkStream stream, HttpRequest request, string query)
    {
        var name = (QueryValue(query, "name") ?? "").Trim();
        if (!IsValidName(name))
        {
            await WriteJsonAsync(stream, 400, new { error = "nome de arquivo invalido" });
            return;
        }

        var (dirFull, relativeDir) = ResolveDirectory(QueryValue(query, "p"));
        var target = Path.Combine(dirFull, name);
        if (Directory.Exists(target))
        {
            await WriteJsonAsync(stream, 409, new { error = "ja existe uma pasta com este nome" });
            return;
        }

        if (request.ContentLength <= 0)
        {
            await WriteJsonAsync(stream, 400, new { error = "arquivo vazio ou tamanho desconhecido" });
            return;
        }

        try
        {
            await using var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var prefix = request.BodyPrefix;
            if (prefix.Length > request.ContentLength)
            {
                prefix = prefix[..(int)request.ContentLength];
            }

            if (prefix.Length > 0)
            {
                await file.WriteAsync(prefix);
            }

            var remaining = request.ContentLength - prefix.Length;
            var buffer = new byte[81920];
            while (remaining > 0)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)));
                if (read <= 0)
                {
                    break;
                }

                await file.WriteAsync(buffer.AsMemory(0, read));
                remaining -= read;
            }
        }
        catch (Exception ex)
        {
            try
            {
                File.Delete(target);
            }
            catch
            {
            }

            await WriteJsonAsync(stream, 500, new { error = ex.Message });
            return;
        }

        var relative = CombineRelative(relativeDir, name);
        _scanCache = null;
        FtpEvents.Add($"Upload web: {relative}");
        await WriteJsonAsync(stream, 200, new { ok = true, name, rel = relative });
    }

    private async Task DeleteAsync(NetworkStream stream, string query)
    {
        var relative = (QueryValue(query, "path") ?? "").Replace('\\', '/').Trim('/');
        if (relative.Length == 0 || relative.Split('/').Any(part => part == ".."))
        {
            await WriteJsonAsync(stream, 400, new { error = "caminho invalido" });
            return;
        }

        try
        {
            var root = Path.GetFullPath(_config.RootPath);
            var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(EnsureTrailingSeparator(root), StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(stream, 400, new { error = "caminho invalido" });
                return;
            }

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
            else if (File.Exists(target))
            {
                File.Delete(target);
            }
            else
            {
                await WriteJsonAsync(stream, 404, new { error = "arquivo ou pasta nao encontrado" });
                return;
            }
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(stream, 500, new { error = ex.Message });
            return;
        }

        _scanCache = null;
        FtpEvents.Add($"Excluido web: {relative}");
        await WriteJsonAsync(stream, 200, new { ok = true });
    }

    private async Task MkdirAsync(NetworkStream stream, string query)
    {
        var name = (QueryValue(query, "name") ?? "").Trim();
        if (!IsValidName(name))
        {
            await WriteJsonAsync(stream, 400, new { error = "nome de pasta invalido" });
            return;
        }

        var (dirFull, relativeDir) = ResolveDirectory(QueryValue(query, "p"));
        var target = Path.Combine(dirFull, name);
        if (File.Exists(target) || Directory.Exists(target))
        {
            await WriteJsonAsync(stream, 409, new { error = "ja existe um item com este nome" });
            return;
        }

        try
        {
            Directory.CreateDirectory(target);
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(stream, 500, new { error = ex.Message });
            return;
        }

        var relative = CombineRelative(relativeDir, name);
        _scanCache = null;
        FtpEvents.Add($"Pasta criada web: {relative}");
        await WriteJsonAsync(stream, 200, new { ok = true, name, rel = relative });
    }

    private async Task DownloadAsync(NetworkStream stream, HttpRequest request, string relative, bool inline)
    {
        var root = Path.GetFullPath(_config.RootPath);
        var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(EnsureTrailingSeparator(root), StringComparison.OrdinalIgnoreCase) || !File.Exists(target))
        {
            await WriteResponseAsync(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("arquivo nao encontrado"), "no-store");
            return;
        }

        var info = new FileInfo(target);
        var safeName = info.Name.Replace("\"", "");
        var asciiName = new string(safeName.Select(c => c is >= (char)32 and < (char)127 ? c : '_').ToArray());
        var hasRange = TryParseRange(request, info.Length, out var start, out var end);
        var length = end - start + 1;
        var status = hasRange ? 206 : 200;
        var contentType = inline ? MimeFor(info.Extension) : "application/octet-stream";
        var disposition = inline ? "inline" : "attachment";

        var header = new StringBuilder();
        header.Append($"HTTP/1.1 {status} {(hasRange ? "Partial Content" : "OK")}\r\n");
        header.Append($"Content-Type: {contentType}\r\n");
        header.Append($"Content-Length: {length}\r\n");
        header.Append("Accept-Ranges: bytes\r\n");
        if (hasRange)
        {
            header.Append($"Content-Range: bytes {start}-{end}/{info.Length}\r\n");
        }

        header.Append($"Content-Disposition: {disposition}; filename=\"{asciiName}\"; filename*=UTF-8''{Uri.EscapeDataString(safeName)}\r\n");
        header.Append("X-Content-Type-Options: nosniff\r\n");
        header.Append("Cache-Control: private, max-age=0\r\n");
        header.Append("Connection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header.ToString()));

        await using var file = File.OpenRead(target);
        file.Seek(start, SeekOrigin.Begin);
        var remaining = length;
        var buffer = new byte[81920];
        while (remaining > 0)
        {
            var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)));
            if (read <= 0)
            {
                break;
            }

            await stream.WriteAsync(buffer.AsMemory(0, read));
            remaining -= read;
        }

        FtpEvents.Add($"Download web: {relative}");
    }

    private static bool TryParseRange(HttpRequest request, long fileLength, out long start, out long end)
    {
        start = 0;
        end = fileLength - 1;
        if (fileLength <= 0 ||
            !request.Headers.TryGetValue("Range", out var header) ||
            string.IsNullOrWhiteSpace(header) ||
            !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var spec = header[6..].Split(',')[0].Trim();
        var dash = spec.IndexOf('-');
        if (dash < 0)
        {
            return false;
        }

        var startText = spec[..dash].Trim();
        var endText = spec[(dash + 1)..].Trim();

        if (startText.Length == 0)
        {
            if (!long.TryParse(endText, out var suffix) || suffix <= 0)
            {
                return false;
            }

            suffix = Math.Min(suffix, fileLength);
            start = fileLength - suffix;
            end = fileLength - 1;
            return true;
        }

        if (!long.TryParse(startText, out start) || start < 0 || start >= fileLength)
        {
            return false;
        }

        if (endText.Length > 0)
        {
            if (!long.TryParse(endText, out end) || end < start)
            {
                return false;
            }

            if (end >= fileLength)
            {
                end = fileLength - 1;
            }
        }
        else
        {
            end = fileLength - 1;
        }

        return true;
    }

    private static string MimeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".jfif" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".avif" => "image/avif",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        ".mp4" => "video/mp4",
        ".m4v" => "video/x-m4v",
        ".webm" => "video/webm",
        ".ogv" or ".ogg" => "video/ogg",
        ".mov" => "video/quicktime",
        ".avi" => "video/x-msvideo",
        ".mkv" => "video/x-matroska",
        ".wmv" => "video/x-ms-wmv",
        ".3gp" => "video/3gpp",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".m4a" => "audio/mp4",
        ".pdf" => "application/pdf",
        ".txt" or ".log" or ".md" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };

    private static Task WriteJsonAsync(NetworkStream stream, int status, object payload) =>
        WriteResponseAsync(stream, status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)), "no-store");

    private static Task WriteJsonTextAsync(NetworkStream stream, int status, string json) =>
        WriteResponseAsync(stream, status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), "no-store");

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        int status,
        string contentType,
        byte[] body,
        string cacheControl,
        string? extraHeaders = null)
    {
        var reason = status switch
        {
            200 => "OK",
            206 => "Partial Content",
            302 => "Found",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            409 => "Conflict",
            413 => "Payload Too Large",
            429 => "Too Many Requests",
            500 => "Internal Server Error",
            _ => "Error",
        };

        var header = new StringBuilder();
        header.Append($"HTTP/1.1 {status} {reason}\r\n");
        header.Append($"Content-Type: {contentType}\r\n");
        header.Append($"Content-Length: {body.Length}\r\n");
        header.Append($"Cache-Control: {cacheControl}\r\n");
        if (!string.IsNullOrEmpty(extraHeaders))
        {
            header.Append(extraHeaders);
            if (!extraHeaders.EndsWith("\r\n", StringComparison.Ordinal))
            {
                header.Append("\r\n");
            }
        }

        header.Append("Connection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header.ToString()));
        if (body.Length > 0)
        {
            await stream.WriteAsync(body);
        }
    }

    private const string HtmlSecurityHeaders =
        "X-Frame-Options: DENY\r\nReferrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff\r\nPermissions-Policy: camera=(self), microphone=(), geolocation=()";

    private string PageUrl()
    {
        var port = _config.WebPort == 80 ? "" : $":{_config.WebPort}";
        return $"http://{DashboardHost()}{port}/";
    }

    private string PageUrl(HttpRequest request)
    {
        var host = RequestHost(request);
        if (string.IsNullOrEmpty(host))
        {
            return PageUrl();
        }

        return $"{RequestScheme(request)}://{host}/";
    }

    private static string RequestScheme(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Forwarded-Proto", out var header))
        {
            var scheme = header.Split(',')[0].Trim();
            if (string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                return "https";
            }
        }

        return "http";
    }

    private string DashboardHost()
    {
        if (!string.IsNullOrWhiteSpace(_config.PublicAddress))
        {
            return _config.PublicAddress;
        }

        return FtpRuntime.PrimaryLanIp() ?? "127.0.0.1";
    }

    private string FtpHost(HttpRequest request)
    {
        var host = RequestHost(request);
        if (string.IsNullOrEmpty(host))
        {
            return DashboardHost();
        }

        if (host[0] == '[')
        {
            var end = host.IndexOf(']');
            return end > 0 ? host[..(end + 1)] : host;
        }

        var colon = host.IndexOf(':');
        return colon > 0 && colon == host.LastIndexOf(':') ? host[..colon] : host;
    }

    private string FtpUrl(string host) => $"ftp://{host}:{_config.Port}/";

    private static string? RequestHost(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Host", out var header))
        {
            return null;
        }

        var host = header.Trim();
        if (host.Length is 0 or > 255)
        {
            return null;
        }

        foreach (var c in host)
        {
            if (!char.IsLetterOrDigit(c) && c is not ('.' or '-' or ':' or '[' or ']'))
            {
                return null;
            }
        }

        return host;
    }

    private string BuildListing(string? requestedRelative)
    {
        try
        {
            var (full, relative) = ResolveDirectory(requestedRelative);
            var entries = new List<object>();

            foreach (var directory in Directory.EnumerateDirectories(full).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(directory);
                entries.Add(new
                {
                    name,
                    dir = true,
                    rel = CombineRelative(relative, name),
                    sizeText = "",
                    modified = "",
                });
            }

            foreach (var file in Directory.EnumerateFiles(full).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(file);
                entries.Add(new
                {
                    name = info.Name,
                    dir = false,
                    rel = CombineRelative(relative, info.Name),
                    sizeText = FormatSize(info.Length),
                    modified = info.LastWriteTime.ToString("dd/MM/yyyy HH:mm"),
                });
            }

            var parent = relative.Length == 0
                ? null
                : relative.Contains('/') ? relative[..relative.LastIndexOf('/')] : "";
            var payload = new
            {
                path = relative,
                parent,
                time = DateTime.Now.ToString("HH:mm:ss"),
                entries,
            };
            return JsonSerializer.Serialize(payload);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, time = DateTime.Now.ToString("HH:mm:ss"), entries = Array.Empty<object>() });
        }
    }

    private string BuildGallery()
    {
        var scan = GetScan();
        var items = scan.Items
            .OrderByDescending(i => i.ModifiedUtc)
            .Select(i => new
            {
                name = i.Name,
                rel = i.Rel,
                size = i.Size,
                sizeText = FormatSize(i.Size),
                modified = i.ModifiedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                modifiedUtc = i.ModifiedUtc,
                ext = i.Ext,
                video = i.Video,
            })
            .ToList();

        return JsonSerializer.Serialize(new
        {
            time = DateTime.Now.ToString("HH:mm:ss"),
            imagesCount = scan.Images,
            videosCount = scan.Videos,
            truncated = scan.Truncated,
            items,
        });
    }

    private string BuildStats()
    {
        var scan = GetScan();
        return JsonSerializer.Serialize(new
        {
            time = DateTime.Now.ToString("HH:mm:ss"),
            files = scan.Files,
            dirs = scan.Directories,
            images = scan.Images,
            videos = scan.Videos,
            totalBytes = scan.TotalBytes,
            totalSizeText = FormatSize(scan.TotalBytes),
            truncated = scan.Truncated,
        });
    }

    private RootScan GetScan()
    {
        lock (_scanLock)
        {
            if (_scanCache is not null && DateTime.UtcNow - _scanCacheUtc < TimeSpan.FromSeconds(4))
            {
                return _scanCache;
            }

            _scanCache = ScanMedia();
            _scanCacheUtc = DateTime.UtcNow;
            return _scanCache;
        }
    }

    private RootScan ScanMedia()
    {
        var scan = new RootScan();
        var root = Path.GetFullPath(_config.RootPath);
        var stack = new Stack<(string Full, string Rel, int Depth)>();
        stack.Push((root, "", 0));

        while (stack.Count > 0)
        {
            var (full, relative, depth) = stack.Pop();

            string[] subdirectories;
            try
            {
                subdirectories = Directory.GetDirectories(full);
            }
            catch
            {
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                try
                {
                    var attributes = File.GetAttributes(subdirectory);
                    if ((attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0)
                    {
                        continue;
                    }

                    scan.Directories++;
                    if (depth < MaxScanDepth)
                    {
                        stack.Push((subdirectory, CombineRelative(relative, Path.GetFileName(subdirectory)), depth + 1));
                    }
                }
                catch
                {
                }
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(full);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                    {
                        continue;
                    }

                    scan.Files++;
                    scan.TotalBytes += info.Length;
                    var isImage = ImageExtensions.Contains(info.Extension);
                    var isVideo = !isImage && VideoExtensions.Contains(info.Extension);
                    if (!isImage && !isVideo)
                    {
                        continue;
                    }

                    if (isImage)
                    {
                        if (scan.Images >= MaxImages)
                        {
                            scan.Truncated = true;
                            continue;
                        }

                        scan.Images++;
                    }
                    else
                    {
                        if (scan.Videos >= MaxVideos)
                        {
                            scan.Truncated = true;
                            continue;
                        }

                        scan.Videos++;
                    }

                    scan.Items.Add(new MediaItem
                    {
                        Name = info.Name,
                        Rel = CombineRelative(relative, info.Name),
                        Ext = info.Extension.TrimStart('.').ToUpperInvariant(),
                        Size = info.Length,
                        ModifiedUtc = info.LastWriteTimeUtc,
                        Video = isVideo,
                    });
                }
                catch
                {
                }
            }
        }

        return scan;
    }

    private string BuildPage(string query, string sessionUser, HttpRequest request)
    {
        var (_, relative) = ResolveDirectory(QueryValue(query, "p"));
        var host = FtpHost(request);
        return PageTemplate.Value
            .Replace("__PATH_JSON__", JsonForScript(relative))
            .Replace("__PAGE_URL__", WebUtility.HtmlEncode(PageUrl(request)))
            .Replace("__SERVER__", WebUtility.HtmlEncode(host))
            .Replace("__PORT__", _config.Port.ToString())
            .Replace("__FTP_ADDR__", WebUtility.HtmlEncode($"{host}:{_config.Port}"))
            .Replace("__ROOT__", WebUtility.HtmlEncode(_config.RootPath))
            .Replace("__SESSION_USER__", WebUtility.HtmlEncode(sessionUser));
    }

    private string BuildLogin(string? error, string next, HttpRequest request)
    {
        var errorBlock = string.IsNullOrEmpty(error)
            ? ""
            : $"<div class=\"err\" role=\"alert\">{WebUtility.HtmlEncode(error)}</div>";
        return LoginTemplate.Value
            .Replace("__ERROR_BLOCK__", errorBlock)
            .Replace("__NEXT__", WebUtility.HtmlEncode(next))
            .Replace("__LOGIN_URL__", WebUtility.HtmlEncode(PageUrl(request)));
    }

    private static string JsonForScript(string value) =>
        JsonSerializer.Serialize(value)
            .Replace("<", "\\u003c", StringComparison.Ordinal)
            .Replace(">", "\\u003e", StringComparison.Ordinal)
            .Replace("&", "\\u0026", StringComparison.Ordinal);

    private static string LoadEmbeddedTemplate(string fileName)
    {
        var assembly = typeof(WebDashboard).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        if (resource is null)
        {
            throw new InvalidOperationException($"Recurso embutido nao encontrado: {fileName}");
        }

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string CombineRelative(string current, string name) =>
        current.Length == 0 ? name : current + "/" + name;

    private (string Full, string Relative) ResolveDirectory(string? requestedRelative)
    {
        var root = Path.GetFullPath(_config.RootPath);
        var relative = (requestedRelative ?? "").Replace('\\', '/').Trim('/');
        if (relative.Length == 0 || relative.Split('/').Any(part => part is ".." or ""))
        {
            return (root, "");
        }

        var candidate = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSep = EnsureTrailingSeparator(root);
        if (!candidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(candidate))
        {
            return (root, "");
        }

        return (candidate, relative);
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static bool IsValidName(string name) =>
        name.Length is > 0 and <= 255 &&
        name is not "." and not ".." &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !name.EndsWith(' ') &&
        !name.EndsWith('.');

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }

    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var name = eq >= 0 ? pair[..eq] : pair;
            if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return eq >= 0 ? WebUtility.UrlDecode(pair[(eq + 1)..]) : "";
        }

        return null;
    }

    internal static byte[] BuildQr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        var qr = new PngByteQRCode(data);
        return qr.GetGraphic(20);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _listener.Stop();
        }
        catch
        {
        }

        _sessions.Clear();
        _cts.Dispose();
    }
}
