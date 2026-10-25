using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FtpNative.Ui;

internal sealed class InputDialog : Window
{
    private readonly TextBox _input;
    private readonly TextBlock _error;

    public string Value { get; private set; } = "";

    public InputDialog(string title, string label, string initialValue = "")
    {
        Title = title;
        Width = 430;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF7, 0xFB));
        FontFamily = new FontFamily("Segoe UI");

        var stack = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            Margin = new Thickness(0, 0, 0, 7),
        });

        _input = new TextBox
        {
            Text = initialValue,
            FontSize = 14,
            Padding = new Thickness(10, 8, 10, 8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xEA, 0xF2)),
            Background = Brushes.White,
        };
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Accept();
            }
        };
        stack.Children.Add(_input);

        _error = new TextBlock
        {
            Text = "",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        stack.Children.Add(_error);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttons.Children.Add(DialogButton("Cancelar", false, () => { DialogResult = false; }));
        buttons.Children.Add(DialogButton("Confirmar", true, Accept));
        stack.Children.Add(buttons);

        Content = stack;
        Loaded += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }

    private void Accept()
    {
        var value = _input.Text.Trim();
        if (value.Length == 0)
        {
            _error.Text = "Informe um nome.";
            _error.Visibility = Visibility.Visible;
            return;
        }

        Value = value;
        DialogResult = true;
    }

    internal static Button DialogButton(string text, bool accent, Action action)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(8, 0, 0, 0),
            BorderThickness = new Thickness(0),
            Background = accent
                ? new SolidColorBrush(Color.FromRgb(0x1A, 0x6F, 0xEB))
                : new SolidColorBrush(Color.FromRgb(0xED, 0xF1, 0xF7)),
            Foreground = accent
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
            Cursor = Cursors.Hand,
        };
        button.Click += (_, _) => action();
        return button;
    }
}

internal sealed class AccountDialog : Window
{
    private readonly TextBox _user;
    private readonly PasswordBox _password;
    private readonly PasswordBox _confirm;
    private readonly TextBlock _error;

    public string Username { get; private set; } = "";
    public string Password { get; private set; } = "";

    public AccountDialog(string title, bool askUsername, string initialUsername = "")
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF7, 0xFB));
        FontFamily = new FontFamily("Segoe UI");

        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
            Margin = new Thickness(0, 0, 0, 16),
        });

        _user = new TextBox
        {
            Text = initialUsername,
            IsReadOnly = !askUsername,
            FontSize = 14,
            Padding = new Thickness(10, 8, 10, 8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xEA, 0xF2)),
            Background = askUsername ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xF2, 0xF5, 0xF9)),
        };
        stack.Children.Add(Field("Login", _user));

        _password = new PasswordBox
        {
            FontSize = 14,
            Padding = new Thickness(10, 8, 10, 8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xEA, 0xF2)),
            Background = Brushes.White,
        };
        stack.Children.Add(Field("Senha", _password));

        _confirm = new PasswordBox
        {
            FontSize = 14,
            Padding = new Thickness(10, 8, 10, 8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xEA, 0xF2)),
            Background = Brushes.White,
        };
        _confirm.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Accept();
            }
        };
        stack.Children.Add(Field("Confirmar senha", _confirm));

        stack.Children.Add(new TextBlock
        {
            Text = "Minimo de 8 caracteres, com letra maiuscula, numero e caractere especial.",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, -2, 0, 8),
        });

        _error = new TextBlock
        {
            Text = "",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed,
        };
        stack.Children.Add(_error);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        buttons.Children.Add(InputDialog.DialogButton("Cancelar", false, () => { DialogResult = false; }));
        buttons.Children.Add(InputDialog.DialogButton("Confirmar", true, Accept));
        stack.Children.Add(buttons);

        Content = stack;
        Loaded += (_, _) =>
        {
            if (askUsername)
            {
                _user.Focus();
            }
            else
            {
                _password.Focus();
            }
        };
    }

    private void Accept()
    {
        var username = _user.Text.Trim();
        var password = _password.Password;
        var confirm = _confirm.Password;
        if (!AccountRules.IsValidUsername(username))
        {
            ShowError("Login invalido. Use de 3 a 32 letras, numeros, ponto, hifen ou sublinhado.");
            return;
        }

        if (!AccountRules.IsValidPassword(password, out var passwordError))
        {
            ShowError(passwordError);
            return;
        }

        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            ShowError("As senhas nao conferem.");
            return;
        }

        Username = username;
        Password = password;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }

    private static Grid Field(string label, UIElement input)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var text = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            Margin = new Thickness(0, 0, 0, 5),
        };
        Grid.SetRow(text, 0);
        grid.Children.Add(text);
        Grid.SetRow(input, 1);
        grid.Children.Add(input);
        return grid;
    }
}
