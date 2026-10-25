using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;

namespace FtpNative.Ui;

public sealed class MainWindow : Window
{
    private static readonly Brush PageBackground = Col(0xF4, 0xF7, 0xFB);
    private static readonly Brush CardBackground = Brushes.White;
    private static readonly Brush TextMain = Col(0x11, 0x18, 0x27);
    private static readonly Brush TextMuted = Col(0x6B, 0x72, 0x80);
    private static readonly Brush Accent = Col(0x1A, 0x6F, 0xEB);
    private static readonly Brush Success = Col(0x16, 0xA3, 0x4A);
    private static readonly Brush Danger = Col(0xDC, 0x26, 0x26);
    private static readonly Brush MiniBackground = Col(0xED, 0xF1, 0xF7);
    private static readonly Brush MiniForeground = Col(0x37, 0x41, 0x51);
    private static readonly Brush SoftBorder = Col(0xE5, 0xEA, 0xF2);
    private static readonly Brush InputBackground = Col(0xF8, 0xFA, 0xFC);
    private static readonly Brush LogBackground = Col(0xF8, 0xFA, 0xFD);
    private static readonly Brush NavBackground = Col(0xF3, 0xF6, 0xFB);
    private static readonly Brush NavActive = new LinearGradientBrush(
        Color.FromRgb(0x1A, 0x73, 0xE8),
        Color.FromRgb(0x6C, 0x4C, 0xF1),
        new Point(0, 0),
        new Point(1, 1));
    private static readonly Regex RemoteUrlPattern = new(
        @"https://[a-z0-9-]+\.trycloudflare\.com",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private const string CloudflaredDownloadUrl = "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe";
    private const string CloudGeometry = "M6.5,19 A4.5,4.5 0 0 1 5.97,10.03 A6.5,6.5 0 0 1 18.6,9.4 A4.3,4.3 0 0 1 18,19 Z";
    private const string UserPattern = "^[A-Za-z0-9._-]{1,64}$";

    private enum AppModule
    {
        Files,
        Settings,
        Users,
    }

    private readonly string _configPath = ServerConfigStore.DefaultConfigPath;
    private readonly ServerConfig _config;
    private IHost? _host;
    private bool _running;
    private bool _busy;
    private bool _revealPassword;
    private bool _refreshingIps;
    private DispatcherTimer? _timer;
    private DispatcherTimer? _usersTimer;
    private AppModule _module = AppModule.Files;

    private ComboBox _ipSelector = null!;
    private TextBlock _serverValue = null!;
    private TextBlock _userValue = null!;
    private TextBlock _passValue = null!;
    private TextBlock _portValue = null!;
    private TextBlock _webValue = null!;
    private TextBlock _qrCaption = null!;
    private TextBlock _statusText = null!;
    private TextBlock _serviceStatus = null!;
    private TextBox _folderBox = null!;
    private TextBox _loginBox = null!;
    private TextBox _portBox = null!;
    private TextBox _webPortBox = null!;
    private PasswordBox _passwordBox = null!;
    private CheckBox _anonymousCheck = null!;
    private CheckBox _startupCheck = null!;
    private Image _qrImage = null!;
    private Border _statusPill = null!;
    private System.Windows.Shapes.Ellipse _statusDot = null!;
    private Button _startStopButton = null!;
    private Button _installServiceButton = null!;
    private Button _removeServiceButton = null!;
    private Button _applyButton = null!;
    private Button _openWebButton = null!;
    private ListBox _logList = null!;

    private Grid _filesView = null!;
    private Grid _settingsView = null!;
    private Grid _usersView = null!;
    private Button _navFiles = null!;
    private Button _navSettings = null!;
    private Button _navUsers = null!;
    private TextBlock _navUsersLabel = null!;
    private Button _topWebButton = null!;
    private string _filesPath = "";
    private ListBox _filesList = null!;
    private Popup _filesFabMenu = null!;
    private TextBlock _filesPathText = null!;
    private TextBlock _filesSummary = null!;
    private TextBlock _filesEmpty = null!;
    private StackPanel _filesEmptyPanel = null!;
    private Border _filesDropOverlay = null!;
    private Button _filesUpButton = null!;
    private Button _filesOpenButton = null!;
    private Button _filesDownloadButton = null!;
    private Button _filesDeleteButton = null!;
    private List<FileRow> _fileRows = new();

    private ListBox _pendingList = null!;
    private ListBox _usersList = null!;
    private TextBlock _pendingEmpty = null!;
    private TextBlock _usersEmpty = null!;
    private Button _approveButton = null!;
    private Button _rejectButton = null!;
    private Button _resetPasswordButton = null!;
    private Button _removeUserButton = null!;
    private List<WebAccessRequest> _pendingSnapshot = new();
    private List<FtpUserAccount> _usersSnapshot = new();

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private Process? _cloudflared;
    private string? _remoteUrl;
    private bool _remoteBusy;
    private bool _closing;
    private TextBlock _remoteStatus = null!;
    private TextBox _remoteLink = null!;
    private Button _remoteStartButton = null!;
    private Button _remoteStopButton = null!;
    private Button _remoteCopyButton = null!;
    private Button _remoteOpenButton = null!;

    public MainWindow()
    {
        _config = ServerConfigStore.LoadOrCreate(_configPath, "admin", null, out var created, out var generated);
        _config.ConfigPath = _configPath;
        if (created && generated is not null)
        {
            FtpEvents.Add($"Senha inicial do usuario admin: {generated}");
        }

        Title = "FastCloud";
        Width = 1120;
        Height = 780;
        MinWidth = 980;
        MinHeight = 660;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowState = WindowState.Maximized;
        Background = PageBackground;
        FontFamily = new FontFamily("Segoe UI");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        try
        {
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/app.ico"));
        }
        catch
        {
        }

        Content = BuildRoot();

        foreach (var line in FtpEvents.Snapshot())
        {
            AppendLog(line);
        }

        FtpEvents.LineAdded += OnFtpEvent;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();

        _usersTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _usersTimer.Tick += (_, _) =>
        {
            if (_module == AppModule.Users)
            {
                LoadUsersData(keepSelection: true);
            }
        };
        _usersTimer.Start();

        Loaded += async (_, _) => await OnLoadedAsync();
        Closing += OnClosing;

        RefreshCard();
        RefreshStatus();
    }

    private async Task OnLoadedAsync()
    {
        try
        {
            ServerConfigStore.EnsureDirectories(_config);
        }
        catch (Exception ex)
        {
            FtpEvents.Add("Erro ao preparar pastas: " + ex.Message);
        }

        if (!ServiceRunning() && !_running && Directory.Exists(_config.RootPath))
        {
            await StartServersAsync(silent: true);
        }

        LoadFiles();
    }

    private void OnFtpEvent(string line) => Dispatcher.BeginInvoke(() => AppendLog(line));

    private void AppendLog(string line)
    {
        _logList.Items.Add(line);
        while (_logList.Items.Count > 250)
        {
            _logList.Items.RemoveAt(0);
        }

        _logList.ScrollIntoView(line);
    }

    private UIElement BuildRoot()
    {
        var root = new Grid { Margin = new Thickness(20, 16, 20, 10) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = BuildTopBar();
        Grid.SetRow(top, 0);
        root.Children.Add(top);

        var content = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        _settingsView = BuildSettingsView();
        _filesView = BuildFilesView();
        _usersView = BuildUsersView();
        _settingsView.Visibility = Visibility.Collapsed;
        _usersView.Visibility = Visibility.Collapsed;
        content.Children.Add(_settingsView);
        content.Children.Add(_filesView);
        content.Children.Add(_usersView);
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        var footer = new TextBlock
        {
            Text = $"FastCloud v{version}   -   ftp://{FtpRuntime.PrimaryLanIp()}   -   config: {_configPath}",
            FontSize = 11,
            Foreground = TextMuted,
            Margin = new Thickness(2, 10, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        ShowModule(AppModule.Files, load: false);
        return root;
    }

    private Border BuildTopBar()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var badge = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(14),
            Background = new LinearGradientBrush(
                Color.FromRgb(0x1A, 0x73, 0xE8),
                Color.FromRgb(0x6C, 0x4C, 0xF1),
                new Point(0, 0),
                new Point(1, 1)),
            Effect = new DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 3,
                Direction = 270,
                Opacity = 0.3,
                Color = Color.FromRgb(0x1A, 0x73, 0xE8),
            },
            Child = new Viewbox
            {
                Width = 26,
                Height = 26,
                Stretch = Stretch.Uniform,
                Child = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(CloudGeometry),
                    Fill = Brushes.White,
                    Stretch = Stretch.Uniform,
                },
            },
        };
        brand.Children.Add(badge);

        var titles = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock
        {
            Text = "FastCloud",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = TextMain,
            LineHeight = 22,
        });
        titles.Children.Add(new TextBlock
        {
            Text = "Arquivos, acesso e configuracoes",
            FontSize = 11.5,
            Foreground = TextMuted,
            Margin = new Thickness(0, 1, 0, 0),
        });
        brand.Children.Add(titles);
        Grid.SetColumn(brand, 0);
        grid.Children.Add(brand);

        _navFiles = MakeNavButton("Arquivos", AppModule.Files, "\uE8B7", out _);
        _navSettings = MakeNavButton("Configuracoes", AppModule.Settings, "\uE713", out _);
        _navUsers = MakeNavButton("Usuarios", AppModule.Users, "\uE716", out _navUsersLabel);
        var nav = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nav.Children.Add(_navFiles);
        nav.Children.Add(_navSettings);
        nav.Children.Add(_navUsers);
        var navPill = new Border
        {
            Background = NavBackground,
            BorderBrush = Col(0xE8, 0xED, 0xF5),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(4),
            Margin = new Thickness(18, 0, 18, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = nav,
        };
        Grid.SetColumn(navPill, 1);
        grid.Children.Add(navPill);

        _statusDot = new System.Windows.Shapes.Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _statusText = new TextBlock
        {
            Text = "Servidor parado",
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextMain,
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _statusPill = new Border
        {
            Background = Col(0xEE, 0xF1, 0xF5),
            BorderBrush = Col(0xE4, 0xE9, 0xF1),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(13, 8, 13, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { _statusDot, _statusText },
            },
        };

        _topWebButton = MakeIconButton("\uE774", "Abrir pagina web", OpenDashboard);
        var topActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        topActions.Children.Add(_topWebButton);
        topActions.Children.Add(new Border
        {
            Width = 1,
            Height = 26,
            Background = SoftBorder,
            Margin = new Thickness(10, 0, 10, 0),
        });
        topActions.Children.Add(_statusPill);
        Grid.SetColumn(topActions, 2);
        grid.Children.Add(topActions);

        return new Border
        {
            Background = new LinearGradientBrush(
                Colors.White,
                Color.FromRgb(0xF9, 0xFB, 0xFF),
                new Point(0, 0),
                new Point(1, 0)),
            BorderBrush = Col(0xEB, 0xEF, 0xF6),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(14, 11, 14, 11),
            Effect = new DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 3,
                Direction = 270,
                Opacity = 0.08,
                Color = Colors.Black,
            },
            Child = grid,
        };
    }

    private static Button MakeIconButton(string glyph, string tooltip, Action action)
    {
        var content = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 17,
            Foreground = MiniForeground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var button = MakeButton("", NavBackground, MiniForeground, 12);
        button.Content = content;
        button.Width = 36;
        button.Height = 36;
        button.Padding = new Thickness(0);
        button.ToolTip = tooltip;
        System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }

    private Button MakeNavButton(string text, AppModule module, string glyph, out TextBlock label)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 15,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        label = new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(label);

        var button = MakeButton(text, NavBackground, MiniForeground, 12.5);
        button.Content = content;
        button.Padding = new Thickness(13, 8, 13, 8);
        button.Margin = new Thickness(1, 0, 1, 0);
        System.Windows.Automation.AutomationProperties.SetName(button, text);
        button.Click += (_, _) => ShowModule(module);
        return button;
    }

    private void ShowModule(AppModule module, bool load = true)
    {
        _module = module;
        if (module != AppModule.Files && _filesFabMenu is not null)
        {
            _filesFabMenu.IsOpen = false;
        }

        if (_filesView is not null)
        {
            _filesView.Visibility = module == AppModule.Files ? Visibility.Visible : Visibility.Collapsed;
            _settingsView.Visibility = module == AppModule.Settings ? Visibility.Visible : Visibility.Collapsed;
            _usersView.Visibility = module == AppModule.Users ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_navFiles is not null)
        {
            StyleNav(_navFiles, module == AppModule.Files);
            StyleNav(_navSettings, module == AppModule.Settings);
            StyleNav(_navUsers, module == AppModule.Users);
        }

        if (!load)
        {
            return;
        }

        if (module == AppModule.Files)
        {
            LoadFiles();
        }
        else if (module == AppModule.Users)
        {
            LoadUsersData(keepSelection: false);
        }
        else
        {
            RefreshStatus();
        }
    }

    private static void StyleNav(Button button, bool active)
    {
        ApplyButtonColors(button, active ? NavActive : NavBackground);
        button.Foreground = active ? Brushes.White : MiniForeground;
        button.Effect = active
            ? new DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 3,
                Direction = 270,
                Opacity = 0.28,
                Color = Color.FromRgb(0x1A, 0x5C, 0xC8),
            }
            : null;
    }

    private static Border ModuleHeader(string eyebrow, string title, string subtitle, string glyph, UIElement? action = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border
        {
            Width = 46,
            Height = 46,
            CornerRadius = new CornerRadius(14),
            Background = new LinearGradientBrush(
                Color.FromRgb(0xE8, 0xF0, 0xFE),
                Color.FromRgb(0xF1, 0xEC, 0xFE),
                new Point(0, 0),
                new Point(1, 1)),
            Margin = new Thickness(0, 0, 12, 0),
            Child = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 20,
                Foreground = Accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock
        {
            Text = eyebrow,
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Accent,
            Margin = new Thickness(0, 0, 0, 3),
        });
        titles.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = TextMain,
            LineHeight = 22,
        });
        titles.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 12,
            Foreground = TextMuted,
            Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(titles, 1);
        grid.Children.Add(titles);

        if (action is not null)
        {
            if (action is FrameworkElement element)
            {
                element.VerticalAlignment = VerticalAlignment.Center;
                element.Margin = new Thickness(16, 0, 0, 0);
            }

            Grid.SetColumn(action, 2);
            grid.Children.Add(action);
        }

        return new Border
        {
            Background = new LinearGradientBrush(
                Colors.White,
                Color.FromRgb(0xF5, 0xF9, 0xFF),
                new Point(0, 0),
                new Point(1, 1)),
            BorderBrush = SoftBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 12),
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 2,
                Direction = 270,
                Opacity = 0.06,
                Color = Colors.Black,
            },
            Child = grid,
        };
    }

    private Grid BuildSettingsView()
    {
        var outer = new Grid();
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = ModuleHeader(
            "CENTRAL",
            "Configuracoes",
            "Servidor, pasta compartilhada, acesso remoto e rede.",
            "\uE713");
        Grid.SetRow(header, 0);
        outer.Children.Add(header);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(348) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel();
        left.Children.Add(BuildConnectionCard());
        left.Children.Add(BuildStartCard());

        var leftScroll = new ScrollViewer
        {
            Content = left,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetColumn(leftScroll, 0);
        root.Children.Add(leftScroll);

        var right = new StackPanel();
        right.Children.Add(BuildShareCard());
        right.Children.Add(BuildRemoteAccessCard());
        right.Children.Add(BuildAccessCard());
        right.Children.Add(BuildServiceCard());
        right.Children.Add(BuildLogCard());

        var rightScroll = new ScrollViewer
        {
            Content = right,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetColumn(rightScroll, 2);
        root.Children.Add(rightScroll);

        Grid.SetRow(root, 1);
        outer.Children.Add(root);
        return outer;
    }

    private Border BuildConnectionCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(SectionTitle("DADOS DO SERVIDOR"));

        _serverValue = ValueText();
        _ipSelector = new ComboBox
        {
            FontSize = 13.5,
            MinWidth = 150,
            Padding = new Thickness(6, 4, 6, 4),
            BorderBrush = SoftBorder,
            Background = InputBackground,
            VerticalAlignment = VerticalAlignment.Center,
        };
        FillIpSelector();
        _ipSelector.SelectionChanged += (_, _) =>
        {
            if (_refreshingIps)
            {
                return;
            }

            RefreshCard();
        };

        _userValue = ValueText();
        _passValue = ValueText();
        _portValue = ValueText();
        _webValue = ValueText();

        stack.Children.Add(InfoRow("Servidor", _ipSelector, MiniButton("Copiar", () => CopyText(SelectedIp()))));
        stack.Children.Add(InfoRow("Login", _userValue, MiniButton("Copiar", () => CopyText(_config.PrimaryUser?.Username ?? ""))));
        stack.Children.Add(InfoRow(
            "Senha",
            _passValue,
            MiniButton("Ver", () =>
            {
                _revealPassword = !_revealPassword;
                RefreshCard();
            }),
            MiniButton("Copiar", () => CopyText(_config.PrimaryUser?.Password ?? ""))));
        stack.Children.Add(InfoRow("Porta FTP", _portValue));
        _openWebButton = MiniButton("Abrir", OpenDashboard);
        stack.Children.Add(InfoRow("Arquivos na web", _webValue, _openWebButton));

        _qrImage = new Image
        {
            Width = 158,
            Height = 158,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "Abrir a pagina de arquivos compartilhados",
        };
        _qrImage.MouseLeftButtonUp += (_, _) => OpenDashboard();
        _qrCaption = new TextBlock
        {
            Text = "Escaneie para abrir a pagina web do servidor",
            FontSize = 11.5,
            Foreground = TextMuted,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var qrPanel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        qrPanel.Children.Add(new Border
        {
            Background = Brushes.White,
            BorderBrush = SoftBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = _qrImage,
        });
        qrPanel.Children.Add(_qrCaption);
        stack.Children.Add(qrPanel);

        return Card(stack);
    }

    private Border BuildStartCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(SectionTitle("SERVICO LOCAL"));
        stack.Children.Add(new TextBlock
        {
            Text = "Inicie aqui para compartilhar sem instalar o servico do Windows.",
            FontSize = 11.5,
            Foreground = TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        _startStopButton = MakeButton("Iniciar servidor", Success);
        _startStopButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _startStopButton.Click += OnStartStopClicked;
        stack.Children.Add(_startStopButton);

        return Card(stack);
    }

    private Border BuildShareCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(SectionTitle("PASTA COMPARTILHADA"));
        stack.Children.Add(new TextBlock
        {
            Text = "Enquanto o FtpNative estiver ativo, esta pasta fica disponivel para a rede via FTP e pela pagina web.",
            FontSize = 12,
            Foreground = TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _folderBox = InputBox();
        _folderBox.IsReadOnly = true;
        _folderBox.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(_folderBox, 0);
        row.Children.Add(_folderBox);

        var choose = MakeButton("Escolher pasta...", Accent);
        choose.VerticalAlignment = VerticalAlignment.Center;
        choose.Click += async (_, _) => await ChooseFolderAsync();
        Grid.SetColumn(choose, 1);
        row.Children.Add(choose);

        stack.Children.Add(row);
        return Card(stack);
    }

    private Border BuildRemoteAccessCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(SectionTitle("ACESSO REMOTO"));
        stack.Children.Add(new TextBlock
        {
            Text = "Crie um link HTTPS temporario para abrir o FastCloud de qualquer lugar, com o mesmo login.",
            FontSize = 12,
            Foreground = TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        _remoteStatus = new TextBlock
        {
            Text = "Desativado",
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextMuted,
            Margin = new Thickness(0, 0, 0, 6),
        };
        stack.Children.Add(_remoteStatus);

        _remoteLink = InputBox();
        _remoteLink.IsReadOnly = true;
        _remoteLink.Text = "O link aparece aqui depois de ativar.";
        _remoteLink.Foreground = TextMuted;
        stack.Children.Add(_remoteLink);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
        };
        _remoteStartButton = MakeButton("Ativar acesso remoto", Accent, Brushes.White, 12);
        _remoteStartButton.Margin = new Thickness(0, 0, 8, 0);
        _remoteStartButton.Click += async (_, _) => await OnRemoteStartClickedAsync();
        buttons.Children.Add(_remoteStartButton);

        _remoteStopButton = MakeButton("Parar", MiniBackground, MiniForeground, 12);
        _remoteStopButton.Margin = new Thickness(0, 0, 8, 0);
        _remoteStopButton.Click += (_, _) => StopRemoteAccess();
        buttons.Children.Add(_remoteStopButton);

        _remoteCopyButton = MakeButton("Copiar link", MiniBackground, MiniForeground, 12);
        _remoteCopyButton.Margin = new Thickness(0, 0, 8, 0);
        _remoteCopyButton.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_remoteUrl))
            {
                CopyText(_remoteUrl);
            }
        };
        buttons.Children.Add(_remoteCopyButton);

        _remoteOpenButton = MakeButton("Abrir", MiniBackground, MiniForeground, 12);
        _remoteOpenButton.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_remoteUrl))
            {
                Process.Start(new ProcessStartInfo(_remoteUrl) { UseShellExecute = true });
            }
        };
        buttons.Children.Add(_remoteOpenButton);
        stack.Children.Add(buttons);

        stack.Children.Add(new TextBlock
        {
            Text = "Usa Cloudflare Quick Tunnel. O link e publico, mas os arquivos continuam protegidos pelo login. Ele fica ativo enquanto esta janela estiver aberta.",
            FontSize = 10.5,
            Foreground = TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });

        UpdateRemoteButtons();
        return Card(stack);
    }

    private bool RemoteActive()
    {
        try
        {
            return _cloudflared is { HasExited: false };
        }
        catch
        {
            return false;
        }
    }

    private void UpdateRemoteButtons()
    {
        if (_remoteStartButton is null)
        {
            return;
        }

        var active = RemoteActive();
        _remoteStartButton.IsEnabled = !_remoteBusy && !active && _config.WebPort > 0;
        _remoteStopButton.IsEnabled = !_remoteBusy && active;
        _remoteCopyButton.IsEnabled = !_remoteBusy && !string.IsNullOrEmpty(_remoteUrl);
        _remoteOpenButton.IsEnabled = !_remoteBusy && !string.IsNullOrEmpty(_remoteUrl);
    }

    private async Task OnRemoteStartClickedAsync()
    {
        if (_remoteBusy)
        {
            return;
        }

        if (_config.WebPort <= 0)
        {
            MessageBox.Show(this, "Ative a pagina web antes do acesso remoto.", "FastCloud", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!_running && !ServiceRunning())
        {
            MessageBox.Show(this, "Inicie o servidor antes de ativar o acesso remoto.", "FastCloud", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _remoteBusy = true;
        _remoteUrl = null;
        _remoteLink.Text = "Preparando...";
        _remoteStatus.Text = "Preparando Cloudflare...";
        _remoteStatus.Foreground = TextMuted;
        UpdateRemoteButtons();
        try
        {
            var executable = await EnsureCloudflaredAsync();
            if (executable is null)
            {
                _remoteStatus.Text = "Nao foi possivel preparar o Cloudflare.";
                _remoteStatus.Foreground = Danger;
                return;
            }

            _remoteStatus.Text = "Conectando ao Cloudflare...";
            StartCloudflared(executable);
        }
        catch (Exception ex)
        {
            _remoteStatus.Text = "Falha ao iniciar o acesso remoto.";
            _remoteStatus.Foreground = Danger;
            FtpEvents.Add("Acesso remoto: " + ex.Message);
            MessageBox.Show(this, "Falha ao iniciar o acesso remoto:\n" + ex.Message, "FastCloud", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _remoteBusy = false;
            UpdateRemoteButtons();
        }
    }

    private async Task<string?> EnsureCloudflaredAsync()
    {
        var existing = FindCloudflared();
        if (existing is not null)
        {
            return existing;
        }

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FastCloud");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "cloudflared.exe");
        var temp = target + ".download";
        _remoteStatus.Text = "Baixando Cloudflare (primeira vez)...";
        try
        {
            using var response = await _http.GetAsync(CloudflaredDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await input.CopyToAsync(output);
            }

            if (!File.Exists(temp) || new FileInfo(temp).Length < 1024)
            {
                throw new InvalidOperationException("Download incompleto.");
            }

            File.Move(temp, target, overwrite: true);
            FtpEvents.Add("Cloudflare baixado para " + target);
            return target;
        }
        catch (Exception ex)
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
            }

            FtpEvents.Add("Falha ao baixar Cloudflare: " + ex.Message);
            MessageBox.Show(
                this,
                "Nao foi possivel baixar o Cloudflare agora.\nVerifique a internet e tente de novo.\n\n" + ex.Message,
                "FastCloud",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return null;
        }
    }

    private static string? FindCloudflared()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "cloudflared.exe");
        if (File.Exists(local))
        {
            return local;
        }

        var installed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FastCloud",
            "cloudflared.exe");
        return File.Exists(installed) ? installed : null;
    }

    private void StartCloudflared(string executable)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = $"tunnel --url http://127.0.0.1:{_config.WebPort} --no-autoupdate --protocol http2",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        process.OutputDataReceived += (_, e) => HandleCloudflaredLine(e.Data);
        process.ErrorDataReceived += (_, e) => HandleCloudflaredLine(e.Data);
        process.Exited += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(_cloudflared, process))
            {
                return;
            }

            _cloudflared = null;
            _remoteUrl = null;
            if (!_closing)
            {
                _remoteLink.Text = "Link encerrado. Ative novamente.";
                _remoteStatus.Text = "Desativado";
                _remoteStatus.Foreground = TextMuted;
                FtpEvents.Add("Acesso remoto encerrado.");
            }

            UpdateRemoteButtons();
        });

        _cloudflared = process;
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Nao foi possivel iniciar o Cloudflare.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch
        {
            _cloudflared = null;
            process.Dispose();
            throw;
        }

        UpdateRemoteButtons();
    }

    private void HandleCloudflaredLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var text = line.Trim();
        if (text.Contains("ERR", StringComparison.OrdinalIgnoreCase))
        {
            FtpEvents.Add("Cloudflare: " + text);
        }

        var match = RemoteUrlPattern.Match(text);
        if (match.Success)
        {
            var url = match.Value;
            Dispatcher.BeginInvoke(() => SetRemoteUrl(url));
        }
    }

    private void SetRemoteUrl(string url)
    {
        if (_closing || string.Equals(_remoteUrl, url, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _remoteUrl = url;
        _remoteLink.Text = url;
        _remoteStatus.Text = "Ativo - link HTTPS gerado";
        _remoteStatus.Foreground = Success;
        FtpEvents.Add("Acesso remoto ativo: " + url);
        UpdateRemoteButtons();
    }

    private void StopRemoteAccess()
    {
        _remoteUrl = null;
        var process = _cloudflared;
        _cloudflared = null;
        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            try
            {
                process.Dispose();
            }
            catch
            {
            }
        }

        if (_remoteLink is not null)
        {
            _remoteLink.Text = "O link aparece aqui depois de ativar.";
        }

        if (_remoteStatus is not null)
        {
            _remoteStatus.Text = "Desativado";
            _remoteStatus.Foreground = TextMuted;
        }

        UpdateRemoteButtons();
    }

    private Border BuildAccessCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(SectionTitle("ACESSO E REDE"));

        _loginBox = InputBox();
        _passwordBox = new PasswordBox
        {
            FontSize = 13,
            Padding = new Thickness(9, 6, 9, 6),
            BorderBrush = SoftBorder,
            Background = InputBackground,
        };
        _anonymousCheck = new CheckBox
        {
            Content = "Permitir acesso anonimo (sem senha)",
            FontSize = 12.5,
            Foreground = TextMain,
            Margin = new Thickness(0, 2, 0, 4),
        };

        _portBox = InputBox();
        _portBox.Width = 72;
        _webPortBox = InputBox();
        _webPortBox.Width = 72;

        stack.Children.Add(Field("Login", _loginBox));
        stack.Children.Add(Field("Nova senha", _passwordBox));
        stack.Children.Add(new TextBlock
        {
            Text = "Deixe a senha vazia para manter a atual. O login e as portas valem para todos os usuarios.",
            FontSize = 11,
            Foreground = TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        stack.Children.Add(_anonymousCheck);
        stack.Children.Add(Field("Portas", BuildPortInputs()));

        _applyButton = MakeButton("Aplicar alteracoes", Accent);
        _applyButton.HorizontalAlignment = HorizontalAlignment.Left;
        _applyButton.Click += OnApplyClicked;
        stack.Children.Add(_applyButton);

        return Card(stack);
    }

    private UIElement BuildPortInputs()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock
        {
            Text = "FTP",
            FontSize = 11.5,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        panel.Children.Add(_portBox);
        panel.Children.Add(new TextBlock
        {
            Text = "Web",
            FontSize = 11.5,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 6, 0),
        });
        panel.Children.Add(_webPortBox);
        panel.Children.Add(new TextBlock
        {
            Text = "(0 desativa)",
            FontSize = 11,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 0, 0),
        });
        return panel;
    }

    private Border BuildServiceCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(SectionTitle("INICIAR COM O WINDOWS"));

        _startupCheck = new CheckBox
        {
            Content = "Abrir o FtpNative junto com o Windows",
            FontSize = 12.5,
            Foreground = TextMain,
            Margin = new Thickness(0, 0, 0, 6),
        };
        _startupCheck.Click += OnStartupClicked;
        stack.Children.Add(_startupCheck);

        stack.Children.Add(new Border
        {
            Height = 1,
            Background = SoftBorder,
            Margin = new Thickness(0, 8, 0, 10),
        });

        _serviceStatus = new TextBlock
        {
            Text = "Servico nao instalado",
            FontSize = 12.5,
            Foreground = TextMuted,
            Margin = new Thickness(0, 0, 0, 8),
        };
        stack.Children.Add(_serviceStatus);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        _installServiceButton = MakeButton("Instalar como servico do Windows", MiniBackground, MiniForeground, 12);
        _installServiceButton.Margin = new Thickness(0, 0, 8, 0);
        _installServiceButton.Click += async (_, _) => await InstallServiceAsync();
        buttons.Children.Add(_installServiceButton);

        _removeServiceButton = MakeButton("Remover", MiniBackground, MiniForeground, 12);
        _removeServiceButton.Click += async (_, _) => await RemoveServiceAsync();
        buttons.Children.Add(_removeServiceButton);
        stack.Children.Add(buttons);

        stack.Children.Add(new TextBlock
        {
            Text = "O servico serve o FTP sem precisar da janela aberta e inicia sozinho ao ligar o computador.",
            FontSize = 11,
            Foreground = TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });

        return Card(stack);
    }

    private Border BuildLogCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(SectionTitle("EVENTOS"));
        _logList = new ListBox
        {
            Height = 150,
            FontSize = 11,
            Background = LogBackground,
            BorderBrush = SoftBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4),
        };
        stack.Children.Add(_logList);
        return Card(stack);
    }

    private Grid BuildFilesView()
    {
        var view = new Grid();
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var pathGrid = new Grid();
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _filesUpButton = MakeButton("Acima", MiniBackground, MiniForeground, 12);
        _filesUpButton.Margin = new Thickness(0, 0, 12, 0);
        _filesUpButton.VerticalAlignment = VerticalAlignment.Center;
        _filesUpButton.IsEnabled = false;
        _filesUpButton.Click += (_, _) => GoUpFolder();
        Grid.SetColumn(_filesUpButton, 0);
        pathGrid.Children.Add(_filesUpButton);

        var pathStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _filesPathText = new TextBlock
        {
            Text = "raiz",
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextMain,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _filesSummary = new TextBlock
        {
            Text = "",
            FontSize = 11.5,
            Foreground = TextMuted,
            Margin = new Thickness(0, 2, 0, 0),
        };
        pathStack.Children.Add(_filesPathText);
        pathStack.Children.Add(_filesSummary);
        Grid.SetColumn(pathStack, 1);
        pathGrid.Children.Add(pathStack);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _filesOpenButton = MakeButton("Abrir", MiniBackground, MiniForeground, 12);
        _filesOpenButton.Margin = new Thickness(0, 0, 8, 0);
        _filesOpenButton.IsEnabled = false;
        _filesOpenButton.Click += (_, _) => OpenSelectedFile();
        actions.Children.Add(_filesOpenButton);

        _filesDownloadButton = MakeButton("Baixar", MiniBackground, MiniForeground, 12);
        _filesDownloadButton.Margin = new Thickness(0, 0, 8, 0);
        _filesDownloadButton.IsEnabled = false;
        _filesDownloadButton.Click += (_, _) => DownloadSelectedFile();
        actions.Children.Add(_filesDownloadButton);

        _filesDeleteButton = MakeButton("Excluir", Danger, Brushes.White, 12);
        _filesDeleteButton.IsEnabled = false;
        _filesDeleteButton.Click += (_, _) => DeleteSelectedFile();
        actions.Children.Add(_filesDeleteButton);
        Grid.SetColumn(actions, 2);
        pathGrid.Children.Add(actions);

        var pathCard = new Border
        {
            Background = Brushes.White,
            BorderBrush = SoftBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 12),
            Child = pathGrid,
        };
        Grid.SetRow(pathCard, 0);
        view.Children.Add(pathCard);

        _filesList = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = 13,
            ItemTemplate = CreateFileCardTemplate(),
            Padding = new Thickness(2, 4, 2, 78),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_filesList, ScrollBarVisibility.Disabled);
        ScrollViewer.SetCanContentScroll(_filesList, false);
        var wrap = new FrameworkElementFactory(typeof(WrapPanel));
        wrap.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);
        _filesList.ItemsPanel = new ItemsPanelTemplate(wrap);

        var cardBorder = new FrameworkElementFactory(typeof(Border));
        cardBorder.Name = "Card";
        cardBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        cardBorder.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        cardBorder.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        cardBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(16));
        cardBorder.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        cardBorder.SetValue(Border.SnapsToDevicePixelsProperty, true);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        cardBorder.AppendChild(presenter);

        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = cardBorder }));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
        itemStyle.Setters.Add(new Setter(Control.BorderBrushProperty, SoftBorder));
        itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty, 176.0));
        itemStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 176.0));
        itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 12, 12)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        itemStyle.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, Col(0xF7, 0xFA, 0xFF)));
        hover.Setters.Add(new Setter(Control.BorderBrushProperty, Col(0xD6, 0xE3, 0xF7)));
        itemStyle.Triggers.Add(hover);
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, Col(0xE8, 0xF0, 0xFE)));
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, Accent));
        itemStyle.Triggers.Add(selected);
        _filesList.ItemContainerStyle = itemStyle;
        _filesList.SelectionChanged += (_, _) => UpdateFileButtons();
        _filesList.MouseDoubleClick += (_, _) => OpenSelectedFile();
        _filesList.KeyDown += OnFilesKeyDown;

        _filesEmpty = new TextBlock
        {
            Text = "Pasta vazia.\nEnvie um arquivo, crie uma pasta ou arraste arquivos para ca.",
            FontSize = 12.5,
            Foreground = TextMuted,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        _filesEmptyPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        _filesEmptyPanel.Children.Add(new TextBlock
        {
            Text = "\uE8B7",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 38,
            Foreground = Col(0xC9, 0xD6, 0xE8),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        _filesEmptyPanel.Children.Add(_filesEmpty);

        _filesDropOverlay = new Border
        {
            Background = Col(0xE8, 0xF0, 0xFE),
            BorderBrush = Accent,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(6),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "\uE8B7",
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize = 34,
                        Foreground = Accent,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = "Solte os arquivos para enviar para esta pasta",
                        FontSize = 14,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Accent,
                        Margin = new Thickness(0, 8, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            },
        };

        var fab = MakeFilesFab();
        _filesFabMenu = BuildFilesFabMenu(fab);
        fab.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_filesFabMenu.IsOpen)
            {
                _filesFabMenu.IsOpen = false;
                e.Handled = true;
            }
        };
        fab.Click += (_, _) => _filesFabMenu.IsOpen = true;

        var listArea = new Grid();
        listArea.Children.Add(_filesList);
        listArea.Children.Add(_filesEmptyPanel);
        listArea.Children.Add(_filesDropOverlay);
        listArea.Children.Add(fab);

        var listCard = Card(listArea);
        listCard.Margin = new Thickness(0);
        listCard.AllowDrop = true;
        listCard.DragEnter += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                _filesDropOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        };
        listCard.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        listCard.DragLeave += (_, e) =>
        {
            var point = e.GetPosition(listCard);
            if (point.X <= 0 || point.Y <= 0 || point.X >= listCard.ActualWidth || point.Y >= listCard.ActualHeight)
            {
                _filesDropOverlay.Visibility = Visibility.Collapsed;
            }
        };
        listCard.Drop += (_, e) =>
        {
            _filesDropOverlay.Visibility = Visibility.Collapsed;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                CopyFilesToCurrent(files);
            }

            e.Handled = true;
        };
        Grid.SetRow(listCard, 1);
        view.Children.Add(listCard);

        return view;
    }

    private static Button MakeFilesFab()
    {
        var plus = new TextBlock
        {
            Text = "\uE710",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 23,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "FabBorder";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(29));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Col(0x15, 0x5C, 0xC8), "FabBorder"));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, Col(0x10, 0x49, 0xA8), "FabBorder"));
        template.Triggers.Add(pressed);

        var button = new Button
        {
            Width = 58,
            Height = 58,
            Background = Accent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 24, 24),
            Cursor = Cursors.Hand,
            ToolTip = "Enviar arquivos ou criar pasta",
            Content = plus,
            Template = template,
            Effect = new DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 4,
                Direction = 270,
                Opacity = 0.32,
                Color = Color.FromRgb(0x10, 0x49, 0xA8),
            },
        };
        System.Windows.Automation.AutomationProperties.SetName(button, "Adicionar arquivos ou pasta");
        return button;
    }

    private Popup BuildFilesFabMenu(Button target)
    {
        var menu = new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.Top,
            HorizontalOffset = 0,
            VerticalOffset = 8,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.Fade,
        };

        var stack = new StackPanel { Width = 260 };
        stack.Children.Add(new TextBlock
        {
            Text = "ADICIONAR",
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextMuted,
            Margin = new Thickness(10, 6, 10, 5),
        });

        var upload = MakeFabMenuItem("\uE898", "Enviar arquivos", "Escolher arquivos do computador", () =>
        {
            menu.IsOpen = false;
            UploadFiles();
        });
        stack.Children.Add(upload);

        var folder = MakeFabMenuItem("\uE8F4", "Nova pasta", "Criar uma pasta nesta localizacao", () =>
        {
            menu.IsOpen = false;
            CreateFolder();
        });
        stack.Children.Add(folder);

        stack.Children.Add(new Border
        {
            Height = 1,
            Background = SoftBorder,
            Margin = new Thickness(8, 5, 8, 5),
        });

        var more = MakeFabMenuItem("\uE712", "Mais", "Atualizar, abrir, baixar ou excluir", () =>
        {
            menu.IsOpen = false;
            ShowFilesMoreMenu(target);
        });
        stack.Children.Add(more);

        menu.Child = new Border
        {
            Background = Brushes.White,
            BorderBrush = SoftBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(7),
            Margin = new Thickness(12),
            Effect = new DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 5,
                Direction = 270,
                Opacity = 0.18,
                Color = Colors.Black,
            },
            Child = stack,
        };
        return menu;
    }

    private static Button MakeFabMenuItem(string glyph, string title, string subtitle, Action action)
    {
        var icon = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(10),
            Background = Col(0xED, 0xF4, 0xFF),
            Child = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Foreground = Accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(icon, 0);

        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(11, 0, 0, 0),
        };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextMain,
        });
        text.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 10.5,
            Foreground = TextMuted,
            Margin = new Thickness(0, 1, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(text, 1);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(icon);
        grid.Children.Add(text);

        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "MenuBorder";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Col(0xF3, 0xF7, 0xFF), "MenuBorder"));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, Col(0xE8, 0xF0, 0xFE), "MenuBorder"));
        template.Triggers.Add(pressed);

        var button = new Button
        {
            Content = grid,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 7, 8, 7),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand,
            Template = template,
        };
        button.Click += (_, _) => action();
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        return button;
    }

    private void ShowFilesMoreMenu(Button target)
    {
        var row = SelectedFile();
        var menu = new ContextMenu
        {
            PlacementTarget = target,
            Placement = PlacementMode.Top,
            FontSize = 12.5,
        };

        var refresh = new MenuItem { Header = "Atualizar lista" };
        refresh.Click += (_, _) => LoadFiles();
        menu.Items.Add(refresh);

        menu.Items.Add(new Separator());

        var open = new MenuItem { Header = "Abrir selecionado", IsEnabled = row is not null };
        open.Click += (_, _) => OpenSelectedFile();
        menu.Items.Add(open);

        var download = new MenuItem { Header = "Baixar selecionado", IsEnabled = row is not null && !row.IsDirectory };
        download.Click += (_, _) => DownloadSelectedFile();
        menu.Items.Add(download);

        var delete = new MenuItem { Header = "Excluir selecionado", IsEnabled = row is not null };
        delete.Click += (_, _) => DeleteSelectedFile();
        menu.Items.Add(delete);

        menu.IsOpen = true;
    }

    private Grid BuildUsersView()
    {
        var view = new Grid();
        view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var refresh = MakeButton("Atualizar", MiniBackground, MiniForeground, 12.5);
        refresh.VerticalAlignment = VerticalAlignment.Center;
        refresh.Click += (_, _) => LoadUsersData(keepSelection: true);
        var header = ModuleHeader(
            "ACESSO",
            "Usuarios",
            "Aprove solicitacoes da web e gerencie quem pode acessar os arquivos.",
            "\uE716",
            refresh);
        Grid.SetRow(header, 0);
        view.Children.Add(header);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var pending = BuildPendingCard();
        Grid.SetColumn(pending, 0);
        content.Children.Add(pending);

        var users = BuildUsersCard();
        Grid.SetColumn(users, 2);
        content.Children.Add(users);

        Grid.SetRow(content, 1);
        view.Children.Add(content);
        return view;
    }

    private Border BuildPendingCard()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = SectionTitle("SOLICITACOES DE ACESSO PELA WEB");
        Grid.SetRow(title, 0);
        grid.Children.Add(title);

        var description = new TextBlock
        {
            Text = "Cadastros criados na tela de login da web aguardam aprovacao aqui.",
            FontSize = 11.5,
            Foreground = TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(description, 1);
        grid.Children.Add(description);

        _pendingList = new ListBox
        {
            BorderBrush = SoftBorder,
            BorderThickness = new Thickness(1),
            Background = LogBackground,
            ItemTemplate = AccountRowTemplate(),
            Padding = new Thickness(4),
            MinHeight = 120,
        };
        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 5, 6, 5)));
        _pendingList.ItemContainerStyle = itemStyle;
        _pendingList.SelectionChanged += (_, _) => _approveButton.IsEnabled = _pendingList.SelectedIndex >= 0;
        _pendingList.MouseDoubleClick += (_, _) => ApproveSelectedRequest();

        _pendingEmpty = new TextBlock
        {
            Text = "Nenhuma solicitacao pendente.",
            FontSize = 12,
            Foreground = TextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(16),
            IsHitTestVisible = false,
        };

        var listArea = new Grid();
        listArea.Children.Add(_pendingList);
        listArea.Children.Add(_pendingEmpty);
        Grid.SetRow(listArea, 2);
        grid.Children.Add(listArea);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        _approveButton = MakeButton("Aprovar", Accent, Brushes.White, 12);
        _approveButton.Margin = new Thickness(0, 0, 8, 0);
        _approveButton.IsEnabled = false;
        _approveButton.Click += (_, _) => ApproveSelectedRequest();
        buttons.Children.Add(_approveButton);

        _rejectButton = MakeButton("Recusar", Danger, Brushes.White, 12);
        _rejectButton.IsEnabled = false;
        _rejectButton.Click += (_, _) => RejectSelectedRequest();
        buttons.Children.Add(_rejectButton);
        Grid.SetRow(buttons, 3);
        grid.Children.Add(buttons);

        _rejectButton.IsEnabled = false;
        _pendingList.SelectionChanged += (_, _) => _rejectButton.IsEnabled = _pendingList.SelectedIndex >= 0;
        return Card(grid);
    }

    private Border BuildUsersCard()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = SectionTitle("USUARIOS CADASTRADOS");
        Grid.SetRow(title, 0);
        grid.Children.Add(title);

        var description = new TextBlock
        {
            Text = "Valem para o FTP e para a pagina web do FastCloud.",
            FontSize = 11.5,
            Foreground = TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(description, 1);
        grid.Children.Add(description);

        _usersList = new ListBox
        {
            BorderBrush = SoftBorder,
            BorderThickness = new Thickness(1),
            Background = LogBackground,
            ItemTemplate = AccountRowTemplate(),
            Padding = new Thickness(4),
            MinHeight = 120,
        };
        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 5, 6, 5)));
        _usersList.ItemContainerStyle = itemStyle;
        _usersList.SelectionChanged += (_, _) =>
        {
            var has = _usersList.SelectedIndex >= 0;
            _resetPasswordButton.IsEnabled = has;
            _removeUserButton.IsEnabled = has;
        };

        _usersEmpty = new TextBlock
        {
            Text = "Nenhum usuario cadastrado.",
            FontSize = 12,
            Foreground = TextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(16),
            IsHitTestVisible = false,
        };

        var listArea = new Grid();
        listArea.Children.Add(_usersList);
        listArea.Children.Add(_usersEmpty);
        Grid.SetRow(listArea, 2);
        grid.Children.Add(listArea);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var add = MakeButton("Adicionar", Accent, Brushes.White, 12);
        add.Margin = new Thickness(0, 0, 8, 0);
        add.Click += (_, _) => AddUser();
        buttons.Children.Add(add);

        _resetPasswordButton = MakeButton("Redefinir senha", MiniBackground, MiniForeground, 12);
        _resetPasswordButton.Margin = new Thickness(0, 0, 8, 0);
        _resetPasswordButton.IsEnabled = false;
        _resetPasswordButton.Click += (_, _) => ResetSelectedPassword();
        buttons.Children.Add(_resetPasswordButton);

        _removeUserButton = MakeButton("Remover", Danger, Brushes.White, 12);
        _removeUserButton.IsEnabled = false;
        _removeUserButton.Click += (_, _) => RemoveSelectedUser();
        buttons.Children.Add(_removeUserButton);
        Grid.SetRow(buttons, 3);
        grid.Children.Add(buttons);

        var note = new TextBlock
        {
            Text = "Aprovacoes e alteracoes reiniciam o servico para valer na hora.",
            FontSize = 10.5,
            Foreground = TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetRow(note, 4);
        grid.Children.Add(note);

        return Card(grid);
    }

    private static DataTemplate CreateFileCardTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(FrameworkElement.MarginProperty, new Thickness(14, 14, 14, 12));

        var icon = new FrameworkElementFactory(typeof(Border));
        icon.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        icon.SetValue(FrameworkElement.WidthProperty, 52.0);
        icon.SetValue(FrameworkElement.HeightProperty, 52.0);
        icon.SetValue(Border.CornerRadiusProperty, new CornerRadius(15));
        icon.SetBinding(Border.BackgroundProperty, new Binding("IconBackground"));

        var glyph = new FrameworkElementFactory(typeof(TextBlock));
        glyph.SetBinding(TextBlock.TextProperty, new Binding("Glyph"));
        glyph.SetBinding(TextBlock.ForegroundProperty, new Binding("IconForeground"));
        glyph.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
        glyph.SetValue(TextBlock.FontSizeProperty, 23.0);
        glyph.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        glyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        icon.AppendChild(glyph);
        panel.AppendChild(icon);

        var text = new FrameworkElementFactory(typeof(StackPanel));
        text.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 18, 0, 0));

        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        name.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Name"));
        name.SetValue(TextBlock.FontSizeProperty, 12.5);
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(TextBlock.ForegroundProperty, TextMain);
        name.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        name.SetValue(FrameworkElement.MaxHeightProperty, 36.0);
        name.SetValue(TextBlock.LineHeightProperty, 17.0);
        text.AppendChild(name);

        var info = new FrameworkElementFactory(typeof(TextBlock));
        info.SetBinding(TextBlock.TextProperty, new Binding("Info"));
        info.SetValue(TextBlock.FontSizeProperty, 10.5);
        info.SetValue(TextBlock.ForegroundProperty, TextMuted);
        info.SetValue(TextBlock.MarginProperty, new Thickness(0, 5, 0, 0));
        info.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        info.SetValue(TextBlock.LineHeightProperty, 14.0);
        info.SetValue(FrameworkElement.MaxHeightProperty, 32.0);
        text.AppendChild(info);

        panel.AppendChild(text);
        return new DataTemplate { VisualTree = panel };
    }

    private static DataTemplate AccountRowTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 2, 2, 2));

        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        name.SetValue(TextBlock.FontSizeProperty, 13.0);
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(TextBlock.ForegroundProperty, TextMain);
        panel.AppendChild(name);

        var info = new FrameworkElementFactory(typeof(TextBlock));
        info.SetBinding(TextBlock.TextProperty, new Binding("Info"));
        info.SetValue(TextBlock.FontSizeProperty, 11.0);
        info.SetValue(TextBlock.ForegroundProperty, TextMuted);
        info.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 0));
        info.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        panel.AppendChild(info);

        return new DataTemplate { VisualTree = panel };
    }

    private void LoadFiles()
    {
        if (_filesList is null)
        {
            return;
        }

        try
        {
            var full = CurrentDirectory();
            var rows = new List<FileRow>();
            long totalSize = 0;
            foreach (var directory in Directory.EnumerateDirectories(full))
            {
                var name = Path.GetFileName(directory);
                rows.Add(new FileRow
                {
                    Glyph = "\uE8B7",
                    Name = name,
                    Info = "Pasta\nArquivos e subpastas",
                    IsDirectory = true,
                    Rel = CombineRelative(_filesPath, name),
                    FullPath = directory,
                    IconBackground = Col(0xFF, 0xF4, 0xD6),
                    IconForeground = Col(0xE3, 0x9B, 0x00),
                });
            }

            foreach (var file in Directory.EnumerateFiles(full))
            {
                var fileInfo = new FileInfo(file);
                var colors = FileIconColors(fileInfo.Extension);
                totalSize += fileInfo.Length;
                rows.Add(new FileRow
                {
                    Glyph = FileGlyph(fileInfo.Extension),
                    Name = fileInfo.Name,
                    Info = $"{FileKind(fileInfo.Extension)} - {FormatSize(fileInfo.Length)}\n{fileInfo.LastWriteTime:dd/MM/yyyy HH:mm}",
                    IsDirectory = false,
                    Rel = CombineRelative(_filesPath, fileInfo.Name),
                    FullPath = file,
                    IconBackground = colors.Background,
                    IconForeground = colors.Foreground,
                });
            }

            _fileRows = rows
                .OrderByDescending(row => row.IsDirectory)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _filesList.ItemsSource = _fileRows;
            _filesPathText.Text = _filesPath.Length == 0
                ? "raiz"
                : "raiz / " + string.Join(" / ", _filesPath.Split('/'));
            var directories = _fileRows.Count(row => row.IsDirectory);
            var files = _fileRows.Count - directories;
            _filesSummary.Text = $"{FormatSize(totalSize)} em arquivos  -  {_config.RootPath}";
            _filesEmptyPanel.Visibility = _fileRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _filesUpButton.IsEnabled = _filesPath.Length > 0;
            UpdateFileButtons();
        }
        catch (Exception ex)
        {
            FtpEvents.Add("Falha ao listar arquivos: " + ex.Message);
            _filesSummary.Text = "Nao foi possivel listar esta pasta.";
            _filesEmptyPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateFileButtons()
    {
        var row = SelectedFile();
        if (_filesOpenButton is null)
        {
            return;
        }

        _filesOpenButton.IsEnabled = row is not null;
        _filesDownloadButton.IsEnabled = row is not null && !row.IsDirectory;
        _filesDeleteButton.IsEnabled = row is not null;
    }

    private FileRow? SelectedFile() => _filesList?.SelectedItem as FileRow;

    private void OpenSelectedFile()
    {
        var row = SelectedFile();
        if (row is null)
        {
            return;
        }

        if (row.IsDirectory)
        {
            _filesPath = row.Rel;
            LoadFiles();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(row.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Nao foi possivel abrir o arquivo:\n" + ex.Message, "FastCloud", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void GoUpFolder()
    {
        if (_filesPath.Length == 0)
        {
            return;
        }

        var slash = _filesPath.LastIndexOf('/');
        _filesPath = slash < 0 ? "" : _filesPath[..slash];
        LoadFiles();
    }

    private void UploadFiles()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Enviar arquivos para " + (_filesPath.Length == 0 ? "raiz" : _filesPath),
                Multiselect = true,
            };

            if (dialog.ShowDialog(this) != true || dialog.FileNames.Length == 0)
            {
                return;
            }

            CopyFilesToCurrent(dialog.FileNames);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao enviar arquivos:\n" + ex.Message, "FastCloud", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyFilesToCurrent(IEnumerable<string> sources)
    {
        try
        {
            var paths = sources.Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
            var files = paths.Where(File.Exists).ToList();
            if (files.Count == 0)
            {
                MessageBox.Show(this, "Arraste arquivos, nao pastas.", "FastCloud", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var destination = CurrentDirectory();
            var existing = files
                .Select(Path.GetFileName)
                .Where(name => name is not null && File.Exists(Path.Combine(destination, name)))
                .ToList();
            var overwrite = false;
            if (existing.Count > 0)
            {
                var result = MessageBox.Show(
                    this,
                    $"{existing.Count} arquivo(s) ja existem nesta pasta. Substituir?",
                    "FastCloud",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                overwrite = true;
            }

            var copied = 0;
            foreach (var source in files)
            {
                var name = Path.GetFileName(source);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var target = SafeChildPath(destination, name);
                if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.Copy(source, target, overwrite);
                copied++;
            }

            if (copied > 0)
            {
                FtpEvents.Add($"Upload local: {copied} arquivo(s) em {(string.IsNullOrEmpty(_filesPath) ? "raiz" : _filesPath)}");
            }

            LoadFiles();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao enviar arquivos:\n" + ex.Message, "FastCloud", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CreateFolder()
    {
        var dialog = new InputDialog("Nova pasta", "Nome da nova pasta:") { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!IsValidFileName(dialog.Value))
        {
            MessageBox.Show(this, "Nome de pasta invalido.", "FastCloud", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var target = SafeChildPath(CurrentDirectory(), dialog.Value);
            if (File.Exists(target) || Directory.Exists(target))
            {
                MessageBox.Show(this, "Ja existe um item com este nome.", "FastCloud", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Directory.CreateDirectory(target);
            FtpEvents.Add($"Pasta local criada: {(string.IsNullOrEmpty(_filesPath) ? dialog.Value : _filesPath + "/" + dialog.Value)}");
            LoadFiles();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao criar pasta:\n" + ex.Message, "FastCloud", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DownloadSelectedFile()
    {
        var row = SelectedFile();
        if (row is null || row.IsDirectory)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Salvar arquivo",
            FileName = row.Name,
            Filter = "Todos os arquivos|*.*",
        };
        if (dialog.ShowDialog(this) != true || string.IsNullOrEmpty(dialog.FileName))
        {
            return;
        }

        try
        {
            File.Copy(row.FullPath, dialog.FileName, overwrite: true);
            FtpEvents.Add($"Download local: {row.Name}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao salvar o arquivo:\n" + ex.Message, "FastCloud", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteSelectedFile()
    {
        var row = SelectedFile();
        if (row is null)
        {
            return;
        }

        var message = row.IsDirectory
            ? $"Excluir a pasta \"{row.Name}\" e todo o seu conteudo?"
            : $"Excluir o arquivo \"{row.Name}\"?";
        if (MessageBox.Show(this, message, "FastCloud", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (row.IsDirectory)
            {
                Directory.Delete(row.FullPath, recursive: true);
            }
            else
            {
                File.Delete(row.FullPath);
            }

            FtpEvents.Add($"Excluido localmente: {row.Rel}");
            LoadFiles();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao excluir:\n" + ex.Message, "FastCloud", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnFilesKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            LoadFiles();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteSelectedFile();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            OpenSelectedFile();
            e.Handled = true;
        }
    }

    private string CurrentDirectory()
    {
        var root = Path.GetFullPath(_config.RootPath);
        Directory.CreateDirectory(root);
        if (_filesPath.Length == 0)
        {
            return root;
        }

        var full = Path.GetFullPath(Path.Combine(root, _filesPath.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Caminho invalido.");
        }

        if (!Directory.Exists(full))
        {
            _filesPath = "";
            return root;
        }

        return full;
    }

    private static string SafeChildPath(string directory, string name)
    {
        var full = Path.GetFullPath(Path.Combine(directory, name));
        var parent = Path.GetFullPath(directory);
        var parentWithSeparator = parent.EndsWith(Path.DirectorySeparatorChar) ? parent : parent + Path.DirectorySeparatorChar;
        if (!full.StartsWith(parentWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Caminho invalido.");
        }

        return full;
    }

    private static string CombineRelative(string current, string name) =>
        current.Length == 0 ? name : current + "/" + name;

    private static bool IsValidFileName(string name) =>
        name.Length is > 0 and <= 255 &&
        name is not "." and not ".." &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !name.EndsWith(' ') &&
        !name.EndsWith('.');

    private static string FileGlyph(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".jfif" or ".png" or ".gif" or ".webp" or ".bmp" or ".avif" or ".svg" => "\uEB9F",
        ".mp4" or ".m4v" or ".webm" or ".ogv" or ".ogg" or ".mov" or ".avi" or ".mkv" or ".wmv" or ".3gp" => "\uE714",
        ".mp3" or ".wav" or ".m4a" or ".flac" => "\uE8D6",
        ".pdf" or ".doc" or ".docx" or ".txt" or ".log" or ".md" => "\uE7C3",
        _ => "\uE7C3",
    };

    private static string FileKind(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".jfif" or ".png" or ".gif" or ".webp" or ".bmp" or ".avif" or ".svg" => "Imagem",
        ".mp4" or ".m4v" or ".webm" or ".ogv" or ".ogg" or ".mov" or ".avi" or ".mkv" or ".wmv" or ".3gp" => "Video",
        ".mp3" or ".wav" or ".m4a" or ".flac" => "Audio",
        ".pdf" => "PDF",
        ".doc" or ".docx" => "Documento",
        ".txt" or ".log" or ".md" => "Texto",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "Compactado",
        ".xls" or ".xlsx" or ".csv" => "Planilha",
        ".ppt" or ".pptx" => "Apresentacao",
        _ => "Arquivo",
    };

    private static (Brush Background, Brush Foreground) FileIconColors(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".jfif" or ".png" or ".gif" or ".webp" or ".bmp" or ".avif" or ".svg" =>
                (Col(0xE6, 0xF4, 0xEA), Col(0x18, 0x80, 0x38)),
            ".mp4" or ".m4v" or ".webm" or ".ogv" or ".ogg" or ".mov" or ".avi" or ".mkv" or ".wmv" or ".3gp" =>
                (Col(0xFC, 0xE8, 0xE6), Col(0xC5, 0x22, 0x1F)),
            ".mp3" or ".wav" or ".m4a" or ".flac" =>
                (Col(0xF3, 0xE8, 0xFD), Col(0x84, 0x30, 0xCE)),
            ".pdf" =>
                (Col(0xFC, 0xE8, 0xE6), Col(0xC5, 0x22, 0x1F)),
            ".xls" or ".xlsx" or ".csv" =>
                (Col(0xE6, 0xF4, 0xEA), Col(0x18, 0x80, 0x38)),
            ".ppt" or ".pptx" =>
                (Col(0xFE, 0xF7, 0xE0), Col(0xB0, 0x60, 0x00)),
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" =>
                (Col(0xFE, 0xF7, 0xE0), Col(0xB0, 0x60, 0x00)),
            _ => (Col(0xE8, 0xF0, 0xFE), Accent),
        };

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

    private void LoadUsersData(bool keepSelection)
    {
        if (_pendingList is null)
        {
            return;
        }

        var pendingIndex = keepSelection ? _pendingList.SelectedIndex : -1;
        var userIndex = keepSelection ? _usersList.SelectedIndex : -1;
        try
        {
            ReloadAccounts();
        }
        catch (Exception ex)
        {
            FtpEvents.Add("Falha ao ler usuarios: " + ex.Message);
        }

        _pendingSnapshot = _config.AccessRequests
            .OrderByDescending(request => request.RequestedUtc)
            .ToList();
        _usersSnapshot = _config.Users.ToList();

        _pendingList.ItemsSource = _pendingSnapshot.Select(request =>
        {
            var requested = DateTime.SpecifyKind(request.RequestedUtc, DateTimeKind.Utc).ToLocalTime();
            var from = string.IsNullOrWhiteSpace(request.RequestedFrom) ? "" : "  -  " + request.RequestedFrom;
            return new AccountRow
            {
                Name = request.Username,
                Info = $"Solicitado em {requested:dd/MM/yyyy HH:mm}{from}",
            };
        }).ToList();

        _usersList.ItemsSource = _usersSnapshot.Select((user, index) =>
        {
            var source = string.IsNullOrEmpty(user.Password) ? "senha definida pela web" : "senha definida no aplicativo";
            var primary = index == 0 ? "  -  usuario principal" : "";
            return new AccountRow
            {
                Name = user.Username,
                Info = source + primary,
            };
        }).ToList();

        _pendingEmpty.Visibility = _pendingSnapshot.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _pendingList.Visibility = _pendingSnapshot.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _usersEmpty.Visibility = _usersSnapshot.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _usersList.Visibility = _usersSnapshot.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (pendingIndex >= 0 && pendingIndex < _pendingSnapshot.Count)
        {
            _pendingList.SelectedIndex = pendingIndex;
        }

        if (userIndex >= 0 && userIndex < _usersSnapshot.Count)
        {
            _usersList.SelectedIndex = userIndex;
        }

        var hasPending = _pendingList.SelectedIndex >= 0;
        _approveButton.IsEnabled = hasPending;
        _rejectButton.IsEnabled = hasPending;
        var hasUser = _usersList.SelectedIndex >= 0;
        _resetPasswordButton.IsEnabled = hasUser;
        _removeUserButton.IsEnabled = hasUser;

        if (_navUsersLabel is not null)
        {
            _navUsersLabel.Text = _pendingSnapshot.Count > 0
                ? $"Usuarios ({_pendingSnapshot.Count})"
                : "Usuarios";
        }
    }

    private void ReloadAccounts()
    {
        var fresh = ServerConfigStore.Load(_configPath);
        _config.Users = fresh.Users;
        _config.AccessRequests = fresh.AccessRequests;
    }

    private void ApproveSelectedRequest()
    {
        var index = _pendingList.SelectedIndex;
        if (index < 0 || index >= _pendingSnapshot.Count)
        {
            return;
        }

        var request = _pendingSnapshot[index];
        var error = "";
        var changed = false;
        try
        {
            ServerConfigStore.Update(_configPath, config =>
            {
                if (config.FindUser(request.Username) is not null)
                {
                    error = "Ja existe um usuario com este login.";
                    return false;
                }

                if (string.IsNullOrEmpty(request.PasswordHash))
                {
                    error = "Solicitacao invalida: senha ausente.";
                    return false;
                }

                if (!config.AccessRequests.Any(item => string.Equals(item.Username, request.Username, StringComparison.OrdinalIgnoreCase)))
                {
                    error = "Solicitacao nao encontrada. Atualize a lista.";
                    return false;
                }

                config.Users.Add(new FtpUserAccount
                {
                    Username = request.Username,
                    Password = "",
                    PasswordHash = request.PasswordHash,
                });
                config.AccessRequests.RemoveAll(item => string.Equals(item.Username, request.Username, StringComparison.OrdinalIgnoreCase));
                changed = true;
                return true;
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao aprovar:\n" + ex.Message, "FastCloud", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!changed)
        {
            MessageBox.Show(this, error, "FastCloud", MessageBoxButton.OK, MessageBoxImage.Warning);
            LoadUsersData(keepSelection: false);
            return;
        }

        FtpEvents.Add($"Acesso web aprovado: {request.Username}");
        _ = FinishAccountChangeAsync($"Usuario \"{request.Username}\" aprovado. Ele ja pode entrar na web e no FTP.");
    }

    private void RejectSelectedRequest()
    {
        var index = _pendingList.SelectedIndex;
        if (index < 0 || index >= _pendingSnapshot.Count)
        {
            return;
        }

        var request = _pendingSnapshot[index];
        if (MessageBox.Show(this, $"Recusar a solicitacao de \"{request.Username}\"?", "FastCloud", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ServerConfigStore.Update(_configPath, config =>
                config.AccessRequests.RemoveAll(item => string.Equals(item.Username, request.Username, StringComparison.OrdinalIgnoreCase)) > 0);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao recusar:\n" + ex.Message, "FastCloud", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        FtpEvents.Add($"Solicitacao web recusada: {request.Username}");
        ReloadAccounts();
        LoadUsersData(keepSelection: false);
    }

    private void AddUser()
    {
        var dialog = new AccountDialog("Adicionar usuario", askUsername: true) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var error = "";
        var changed = false;
        try
        {
            ServerConfigStore.Update(_configPath, config =>
            {
                if (config.FindUser(dialog.Username) is not null)
                {
                    error = "Ja existe um usuario com este login.";
                    return false;
                }

                config.Users.Add(new FtpUserAccount
                {
                    Username = dialog.Username,
                    Password = dialog.Password,
                    PasswordHash = PasswordHasher.Hash(dialog.Password),
                });
                changed = true;
                return true;
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao adicionar usuario:\n" + ex.Message, "FastCloud", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!changed)
        {
            MessageBox.Show(this, error, "FastCloud", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        FtpEvents.Add($"Usuario adicionado: {dialog.Username}");
        _ = FinishAccountChangeAsync($"Usuario \"{dialog.Username}\" adicionado.");
    }

    private void ResetSelectedPassword()
    {
        var index = _usersList.SelectedIndex;
        if (index < 0 || index >= _usersSnapshot.Count)
        {
            return;
        }

        var user = _usersSnapshot[index];
        var dialog = new AccountDialog("Redefinir senha", askUsername: false, initialUsername: user.Username) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var changed = false;
        try
        {
            ServerConfigStore.Update(_configPath, config =>
            {
                var account = config.FindUser(user.Username);
                if (account is null)
                {
                    return false;
                }

                account.Password = dialog.Password;
                account.PasswordHash = PasswordHasher.Hash(dialog.Password);
                changed = true;
                return true;
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao redefinir a senha:\n" + ex.Message, "FastCloud", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!changed)
        {
            MessageBox.Show(this, "Usuario nao encontrado. Atualize a lista.", "FastCloud", MessageBoxButton.OK, MessageBoxImage.Warning);
            LoadUsersData(keepSelection: false);
            return;
        }

        FtpEvents.Add($"Senha redefinida: {user.Username}");
        _ = FinishAccountChangeAsync($"Senha de \"{user.Username}\" redefinida.");
    }

    private void RemoveSelectedUser()
    {
        var index = _usersList.SelectedIndex;
        if (index < 0 || index >= _usersSnapshot.Count)
        {
            return;
        }

        var user = _usersSnapshot[index];
        if (_usersSnapshot.Count <= 1 && !_config.AllowAnonymous)
        {
            MessageBox.Show(this, "Nao e possivel remover o ultimo usuario sem acesso anonimo.", "FastCloud", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(this, $"Remover o usuario \"{user.Username}\"?", "FastCloud", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ServerConfigStore.Update(_configPath, config =>
                config.Users.RemoveAll(item => string.Equals(item.Username, user.Username, StringComparison.OrdinalIgnoreCase)) > 0);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao remover:\n" + ex.Message, "FastCloud", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        FtpEvents.Add($"Usuario removido: {user.Username}");
        _ = FinishAccountChangeAsync($"Usuario \"{user.Username}\" removido.");
    }

    private async Task FinishAccountChangeAsync(string message)
    {
        ReloadAccounts();
        RefreshCard();
        LoadUsersData(keepSelection: false);
        if (_running)
        {
            await StopServersAsync();
            await StartServersAsync(silent: true);
        }
        else if (ServiceRunning())
        {
            await RestartServiceAsync();
            await Task.Delay(900);
        }

        RefreshStatus();
        MessageBox.Show(this, message + (ServiceRunning() || _running ? "\n\nO servico foi reiniciado." : ""), "FastCloud", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private sealed class FileRow
    {
        public string Glyph { get; init; } = "";
        public string Name { get; init; } = "";
        public string Info { get; init; } = "";
        public bool IsDirectory { get; init; }
        public string Rel { get; init; } = "";
        public string FullPath { get; init; } = "";
        public Brush IconBackground { get; init; } = Brushes.White;
        public Brush IconForeground { get; init; } = Brushes.Black;
    }

    private sealed class AccountRow
    {
        public string Name { get; init; } = "";
        public string Info { get; init; } = "";
    }

    private async void OnStartStopClicked(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (_running)
        {
            await StopServersAsync();
        }
        else
        {
            await StartServersAsync();
        }
    }

    private async Task StartServersAsync(bool silent = false)
    {
        if (_busy)
        {
            return;
        }

        if (ServiceRunning())
        {
            if (!silent)
            {
                MessageBox.Show(this, "O servico do Windows ja esta servindo o FTP.", "FtpNative", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return;
        }

        if (!Directory.Exists(_config.RootPath))
        {
            if (!silent)
            {
                MessageBox.Show(this, "Escolha a pasta compartilhada antes de iniciar.", "FtpNative", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        if (_config.Users.Count == 0 && !_config.AllowAnonymous)
        {
            if (!silent)
            {
                MessageBox.Show(this, "Informe um login e senha antes de iniciar.", "FtpNative", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        if (!FtpRuntime.IsPortFree(_config.Port))
        {
            if (!silent)
            {
                MessageBox.Show(this, $"A porta {_config.Port} esta em uso por outro programa.", "FtpNative", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            RefreshStatus();
            return;
        }

        if (_config.WebPort > 0 && _config.WebPort != _config.Port && !FtpRuntime.IsPortFree(_config.WebPort))
        {
            if (!silent)
            {
                MessageBox.Show(this, $"A porta web {_config.WebPort} esta em uso. Altere a porta em Acesso e rede.", "FtpNative", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        _busy = true;
        RefreshStatus();
        try
        {
            _host = FtpRuntime.BuildHost(_config, windowsService: false);
            await _host.StartAsync();
            _running = true;
            FtpEvents.Add("Servidor iniciado pela interface");
        }
        catch (Exception ex)
        {
            _host?.Dispose();
            _host = null;
            _running = false;
            FtpEvents.Add("Falha ao iniciar: " + ex.Message);
            if (!silent)
            {
                MessageBox.Show(this, "Nao foi possivel iniciar o servidor:\n" + ex.Message, "FtpNative", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            _busy = false;
            RefreshStatus();
        }
    }

    private async Task StopServersAsync()
    {
        if (_busy || _host is null)
        {
            return;
        }

        _busy = true;
        RefreshStatus();
        try
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        catch
        {
        }
        finally
        {
            _host = null;
            _running = false;
            _busy = false;
            FtpEvents.Add("Servidor parado pela interface");
            RefreshStatus();
        }
    }

    private async Task ChooseFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Escolha a pasta para compartilhar",
            Multiselect = false,
        };
        if (Directory.Exists(_config.RootPath))
        {
            dialog.InitialDirectory = _config.RootPath;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _config.RootPath = dialog.FolderName;
        _config.UserIsolationPerFolder = false;
        ServerConfigStore.Save(_config, _configPath);
        ServerConfigStore.EnsureDirectories(_config);
        _folderBox.Text = _config.RootPath;
        FtpEvents.Add($"Pasta compartilhada: {_config.RootPath}");

        if (_running)
        {
            await StopServersAsync();
            await StartServersAsync(silent: true);
        }
        else if (ServiceRunning())
        {
            await RestartServiceAsync();
        }

        RefreshCard();
        RefreshStatus();
    }

    private async void OnApplyClicked(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (!SaveFromInputs(showErrors: true))
        {
            return;
        }

        if (_running)
        {
            await StopServersAsync();
            await StartServersAsync(silent: true);
        }
        else if (ServiceRunning())
        {
            await RestartServiceAsync();
        }

        RefreshStatus();
        MessageBox.Show(this, "Alteracoes aplicadas.", "FtpNative", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private bool SaveFromInputs(bool showErrors)
    {
        if (!int.TryParse(_portBox.Text.Trim(), out var port) || port < 1 || port > 65535)
        {
            if (showErrors)
            {
                MessageBox.Show(this, "Porta FTP invalida (use de 1 a 65535).", "FtpNative", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return false;
        }

        if (!int.TryParse(_webPortBox.Text.Trim(), out var webPort) || webPort < 0 || webPort > 65535 || webPort == port)
        {
            if (showErrors)
            {
                MessageBox.Show(this, "Porta web invalida (0 desativa; nao pode ser igual a porta FTP).", "FtpNative", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return false;
        }

        var login = _loginBox.Text.Trim();
        if (login.Length > 0 && !Regex.IsMatch(login, UserPattern))
        {
            if (showErrors)
            {
                MessageBox.Show(this, "Login invalido. Use letras, numeros, ponto, hifen ou sublinhado.", "FtpNative", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return false;
        }

        try
        {
            ReloadAccounts();
        }
        catch
        {
        }

        var anonymous = _anonymousCheck.IsChecked == true;
        var user = _config.PrimaryUser;
        if (user is null && login.Length == 0 && !anonymous)
        {
            if (showErrors)
            {
                MessageBox.Show(this, "Informe um login e senha.", "FtpNative", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return false;
        }

        _config.Port = port;
        _config.WebPort = webPort;
        _config.AllowAnonymous = anonymous;

        var newPassword = _passwordBox.Password;
        if (user is null)
        {
            var pwd = newPassword.Length > 0 ? newPassword : PasswordHasher.NewPassword();
            _config.Users.Add(new FtpUserAccount
            {
                Username = login,
                Password = pwd,
                PasswordHash = PasswordHasher.Hash(pwd),
            });

            if (newPassword.Length == 0)
            {
                FtpEvents.Add($"Senha gerada para {login}: {pwd}");
            }
        }
        else
        {
            if (login.Length > 0 && !string.Equals(login, user.Username, StringComparison.Ordinal))
            {
                var other = _config.FindUser(login);
                if (other is not null && !ReferenceEquals(other, user))
                {
                    if (showErrors)
                    {
                        MessageBox.Show(this, "Ja existe um usuario com este login.", "FtpNative", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    return false;
                }

                user.Username = login;
            }

            if (newPassword.Length > 0)
            {
                user.Password = newPassword;
                user.PasswordHash = PasswordHasher.Hash(newPassword);
            }
        }

        ServerConfigStore.Save(_config, _configPath);
        ServerConfigStore.EnsureDirectories(_config);
        _passwordBox.Password = "";
        RefreshCard();
        return true;
    }

    private async Task InstallServiceAsync()
    {
        if (!SaveFromInputs(showErrors: true))
        {
            return;
        }

        if (Directory.Exists(_config.RootPath) == false)
        {
            MessageBox.Show(this, "Escolha a pasta compartilhada antes de instalar o servico.", "FtpNative", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_running)
        {
            await StopServersAsync();
        }

        var exitCode = await Task.Run(() => InstallerCli.Dispatch(new[] { "install" }));
        if (exitCode == 3)
        {
            var elevated = await RunElevatedAsync("--install");
            if (!elevated)
            {
                MessageBox.Show(this, "A instalacao precisa de administrador e nao foi concluida.", "FtpNative", MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshStatus();
                return;
            }
        }
        else if (exitCode != 0)
        {
            MessageBox.Show(this, "Nao foi possivel instalar o servico. Veja o log em:\n" + ServerConfigStore.LogPath, "FtpNative", MessageBoxButton.OK, MessageBoxImage.Error);
            RefreshStatus();
            return;
        }

        await Task.Delay(800);
        RefreshStatus();
        MessageBox.Show(this, "Servico instalado e iniciado. Ele tambem inicia automaticamente com o Windows.", "FtpNative", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task RemoveServiceAsync()
    {
        if (_running)
        {
            await StopServersAsync();
        }

        var exitCode = await Task.Run(() => InstallerCli.Dispatch(new[] { "uninstall" }));
        if (exitCode == 3)
        {
            var elevated = await RunElevatedAsync("--uninstall");
            if (!elevated)
            {
                MessageBox.Show(this, "A remocao precisa de administrador.", "FtpNative", MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshStatus();
                return;
            }
        }

        await Task.Delay(600);
        RefreshStatus();
        MessageBox.Show(this, "Servico removido. Os arquivos compartilhados foram mantidos.", "FtpNative", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task RestartServiceAsync()
    {
        var exitCode = await Task.Run(() => InstallerCli.Dispatch(new[] { "restart" }));
        if (exitCode == 3)
        {
            await RunElevatedAsync("--restart");
        }
    }

    private async Task<bool> RunElevatedAsync(string arguments)
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath))
            {
                return false;
            }

            var info = new ProcessStartInfo(processPath)
            {
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var process = Process.Start(info);
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private void OnStartupClicked(object sender, RoutedEventArgs e)
    {
        var enable = _startupCheck.IsChecked == true;
        try
        {
            var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var shortcut = Path.Combine(startup, "FtpNative.lnk");
            if (!enable)
            {
                if (File.Exists(shortcut))
                {
                    File.Delete(shortcut);
                }
            }
            else
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell indisponivel.");
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic link = shell.CreateShortcut(shortcut);
                link.TargetPath = Environment.ProcessPath;
                link.WorkingDirectory = Path.GetDirectoryName(Environment.ProcessPath);
                link.Description = "FastCloud - Servidor FTP";
                link.WindowStyle = 1;
                link.Save();
            }

            _config.AutoStartWithWindows = enable;
            ServerConfigStore.Save(_config, _configPath);
            FtpEvents.Add(enable ? "Inicializacao com o Windows ativada" : "Inicializacao com o Windows desativada");
        }
        catch (Exception ex)
        {
            _startupCheck.IsChecked = !enable;
            MessageBox.Show(this, "Nao foi possivel atualizar a inicializacao:\n" + ex.Message, "FtpNative", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _closing = true;
        if (_running)
        {
            var result = MessageBox.Show(this, "O servidor FTP esta ativo. Deseja parar e fechar?", "FtpNative", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                _closing = false;
                e.Cancel = true;
                return;
            }
        }

        StopRemoteAccess();
        if (!_running)
        {
            _http.Dispose();
            return;
        }

        try
        {
            _host?.StopAsync().Wait(3000);
            _host?.Dispose();
        }
        catch
        {
        }

        _host = null;
        _running = false;
        _http.Dispose();
    }

    private void RefreshCard()
    {
        var ip = SelectedIp();
        var user = _config.PrimaryUser;
        _serverValue.Text = ip;
        _userValue.Text = user?.Username ?? "(sem usuario)";
        _passValue.Text = user is null
            ? "-"
            : string.IsNullOrEmpty(user.Password)
                ? "definida pela web"
                : _revealPassword ? user.Password : "********";
        _portValue.Text = _config.Port.ToString();
        _webValue.Text = _config.WebPort > 0 ? $"http://{ip}:{_config.WebPort}/" : "desativado";
        _openWebButton.IsEnabled = _config.WebPort > 0;
        if (_topWebButton is not null)
        {
            _topWebButton.IsEnabled = _config.WebPort > 0;
            _topWebButton.ToolTip = _config.WebPort > 0 ? "Abrir pagina web" : "Pagina web desativada";
        }

        if (_config.WebPort > 0)
        {
            _qrImage.Source = MakeQrImage($"http://{ip}:{_config.WebPort}/");
            _qrCaption.Text = "Escaneie e entre com seu login para ver os arquivos no celular ou PC";
            _qrImage.Opacity = 1;
        }
        else
        {
            _qrImage.Source = MakeQrImage(BuildFtpUrl(ip));
            _qrCaption.Text = "Pagina web desativada - escaneie e abra no app de FTP";
            _qrImage.Opacity = 1;
        }

        _loginBox.Text = user?.Username ?? "";
        _portBox.Text = _config.Port.ToString();
        _webPortBox.Text = _config.WebPort.ToString();
        _anonymousCheck.IsChecked = _config.AllowAnonymous;
        _startupCheck.IsChecked = _config.AutoStartWithWindows;
        _folderBox.Text = _config.RootPath;
        if (_config.WebPort <= 0 && RemoteActive())
        {
            StopRemoteAccess();
        }

        UpdateRemoteButtons();
    }

    private void RefreshStatus()
    {
        if (_timer is null)
        {
            return;
        }

        RefreshIpList();

        var installed = false;
        var serviceState = ServiceState.Unknown;
        try
        {
            installed = WindowsServiceControl.TryGetState(InstallerCli.ServiceName, out serviceState);
        }
        catch
        {
        }

        var serviceRunning = installed && serviceState is ServiceState.Running or ServiceState.StartPending;
        _serviceStatus.Text = installed ? "Servico: " + StateText(serviceState) : "Servico nao instalado";
        _removeServiceButton.IsEnabled = installed;
        _installServiceButton.Content = installed ? "Atualizar servico" : "Instalar como servico do Windows";

        string status;
        Brush color;
        Brush pill;
        if (_busy)
        {
            status = "Aplicando...";
            color = Col(0xB4, 0x53, 0x09);
            pill = Col(0xFE, 0xF3, 0xC7);
        }
        else if (_running)
        {
            status = "Servidor ativo";
            color = Success;
            pill = Col(0xDC, 0xFC, 0xE7);
        }
        else if (serviceRunning)
        {
            status = "Servico ativo";
            color = Success;
            pill = Col(0xDC, 0xFC, 0xE7);
        }
        else if (!FtpRuntime.IsPortFree(_config.Port))
        {
            status = $"Porta {_config.Port} em uso";
            color = Danger;
            pill = Col(0xFE, 0xE2, 0xE2);
        }
        else
        {
            status = "Servidor parado";
            color = TextMuted;
            pill = Col(0xEE, 0xF1, 0xF5);
        }

        _statusText.Text = status;
        _statusDot.Fill = color;
        _statusPill.Background = pill;
        _statusPill.ToolTip = _serviceStatus.Text + "\n" + status;

        if (serviceRunning)
        {
            _startStopButton.Content = "Ativo pelo servico do Windows";
            ApplyButtonColors(_startStopButton, Col(0x9C, 0xA3, 0xAF));
            _startStopButton.IsEnabled = false;
        }
        else if (_running)
        {
            _startStopButton.Content = "Parar servidor";
            ApplyButtonColors(_startStopButton, Danger);
            _startStopButton.IsEnabled = true;
        }
        else
        {
            _startStopButton.Content = "Iniciar servidor";
            ApplyButtonColors(_startStopButton, Success);
            _startStopButton.IsEnabled = !_busy;
        }
    }

    private void RefreshIpList()
    {
        var addresses = FtpRuntime.LanAddresses();
        var primary = FtpRuntime.PrimaryLanIp();
        if (primary is not null && addresses.Remove(primary))
        {
            addresses.Insert(0, primary);
        }

        var current = _ipSelector.Items.Cast<string>().ToList();
        if (current.SequenceEqual(addresses))
        {
            return;
        }

        var selected = _ipSelector.SelectedItem as string;
        _refreshingIps = true;
        try
        {
            _ipSelector.Items.Clear();
            foreach (var address in addresses)
            {
                _ipSelector.Items.Add(address);
            }

            _ipSelector.SelectedItem = selected is not null && addresses.Contains(selected) ? selected : addresses.FirstOrDefault();
        }
        finally
        {
            _refreshingIps = false;
        }

        RefreshCard();
    }

    private void FillIpSelector()
    {
        var addresses = FtpRuntime.LanAddresses();
        var primary = FtpRuntime.PrimaryLanIp();
        if (primary is not null && addresses.Remove(primary))
        {
            addresses.Insert(0, primary);
        }

        foreach (var address in addresses)
        {
            _ipSelector.Items.Add(address);
        }

        _ipSelector.SelectedIndex = 0;
    }

    private string SelectedIp() => _ipSelector.SelectedItem as string ?? FtpRuntime.PrimaryLanIp() ?? "127.0.0.1";

    private string BuildFtpUrl(string ip)
    {
        var user = _config.PrimaryUser;
        if (user is null || string.IsNullOrEmpty(user.Password))
        {
            return $"ftp://{ip}:{_config.Port}/";
        }

        return $"ftp://{Uri.EscapeDataString(user.Username)}:{Uri.EscapeDataString(user.Password)}@{ip}:{_config.Port}/";
    }

    private bool ServiceRunning()
    {
        try
        {
            return WindowsServiceControl.TryGetState(InstallerCli.ServiceName, out var state) &&
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
        _ => "Desconhecido",
    };

    private void OpenDashboard()
    {
        if (_config.WebPort <= 0)
        {
            MessageBox.Show(this, "O painel web esta desativado (porta 0).", "FtpNative", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo($"http://{SelectedIp()}:{_config.WebPort}/") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Nao foi possivel abrir o navegador:\n" + ex.Message, "FtpNative", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
            FtpEvents.Add("Copiado para a area de transferencia");
        }
        catch
        {
        }
    }

    private static BitmapSource? MakeQrImage(string text)
    {
        try
        {
            var bytes = WebDashboard.BuildQr(text);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static SolidColorBrush Col(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = TextMuted,
        Margin = new Thickness(0, 0, 0, 10),
    };

    private static TextBlock ValueText() => new()
    {
        Text = "-",
        FontSize = 14.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = TextMain,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBox InputBox() => new()
    {
        FontSize = 13,
        Padding = new Thickness(9, 6, 9, 6),
        BorderBrush = SoftBorder,
        Background = InputBackground,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    private static Border Card(UIElement content) => new()
    {
        Background = CardBackground,
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(18, 16, 18, 16),
        Margin = new Thickness(0, 0, 0, 14),
        Effect = new DropShadowEffect
        {
            BlurRadius = 22,
            ShadowDepth = 2,
            Direction = 270,
            Opacity = 0.09,
            Color = Colors.Black,
        },
        Child = content,
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        Foreground = TextMuted,
        Margin = new Thickness(0, 0, 0, 1),
    };

    private static Grid InfoRow(string label, UIElement value, params UIElement[] actions)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var stack = new StackPanel();
        stack.Children.Add(Label(label));
        stack.Children.Add(value);
        Grid.SetColumn(stack, 0);
        grid.Children.Add(stack);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var action in actions)
        {
            if (action is FrameworkElement element)
            {
                element.Margin = new Thickness(4, 2, 0, 0);
            }

            actionPanel.Children.Add(action);
        }

        Grid.SetColumn(actionPanel, 1);
        grid.Children.Add(actionPanel);
        return grid;
    }

    private static Grid Field(string label, UIElement input)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var text = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);
        Grid.SetColumn(input, 1);
        grid.Children.Add(input);
        return grid;
    }

    private static Button MiniButton(string text, Action action)
    {
        var button = MakeButton(text, MiniBackground, MiniForeground, 11.5);
        button.Padding = new Thickness(8, 3, 8, 3);
        button.Click += (_, _) => action();
        return button;
    }

    private static Button MakeButton(string text, Brush background, Brush? foreground = null, double fontSize = 13.5)
    {
        var button = new Button
        {
            Content = text,
            Background = background,
            Foreground = foreground ?? Brushes.White,
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 9, 14, 9),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Template = CreateButtonTemplate(background),
        };
        return button;
    }

    private static void ApplyButtonColors(Button button, Brush background)
    {
        button.Background = background;
        button.Template = CreateButtonTemplate(background);
    }

    private static ControlTemplate CreateButtonTemplate(Brush background)
    {
        var template = new ControlTemplate(typeof(Button));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(Border.BackgroundProperty, background);
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        template.VisualTree = border;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Shade(background, 0.9), "Bd"));
        template.Triggers.Add(hover);

        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, Shade(background, 0.82), "Bd"));
        template.Triggers.Add(pressed);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55));
        template.Triggers.Add(disabled);

        return template;
    }

    private static Brush Shade(Brush brush, double factor)
    {
        if (brush is not SolidColorBrush solid)
        {
            return brush;
        }

        var color = solid.Color;
        return new SolidColorBrush(Color.FromRgb(
            (byte)(color.R * factor),
            (byte)(color.G * factor),
            (byte)(color.B * factor)));
    }
}
