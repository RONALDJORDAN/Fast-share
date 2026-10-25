using System.Text.RegularExpressions;

namespace FtpNative;

internal static class AccountRules
{
    private static readonly Regex UsernamePattern = new("^[A-Za-z0-9._-]{3,32}$", RegexOptions.Compiled);

    public static bool IsValidUsername(string username) =>
        !string.IsNullOrWhiteSpace(username) && UsernamePattern.IsMatch(username.Trim());

    public static bool IsValidPassword(string password, out string error)
    {
        if (password.Length < 8)
        {
            error = "A senha precisa ter no minimo 8 caracteres.";
            return false;
        }

        if (password.Length > 128)
        {
            error = "A senha pode ter no maximo 128 caracteres.";
            return false;
        }

        if (!password.Any(char.IsUpper))
        {
            error = "A senha precisa ter pelo menos uma letra maiuscula.";
            return false;
        }

        if (!password.Any(char.IsDigit))
        {
            error = "A senha precisa ter pelo menos um numero.";
            return false;
        }

        if (password.All(char.IsLetterOrDigit))
        {
            error = "A senha precisa ter pelo menos um caractere especial.";
            return false;
        }

        error = "";
        return true;
    }
}
