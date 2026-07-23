namespace XboxGuideTray.UI;

public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _startupMenuItem;
    private readonly ToolStripMenuItem _installHidHideMenuItem;
    private readonly Func<bool> _isHidHideInstalled;

    public TrayIconManager(
        Action onOpenSettings,
        Action onDisconnectController,
        Action onToggleStartup,
        Action onInstallHidHide,
        Func<bool> isHidHideInstalled,
        Action onAbout,
        Action onExit)
    {
        _isHidHideInstalled = isHidHideInstalled;

        _startupMenuItem = new ToolStripMenuItem("Run at Startup", null, (_, _) => onToggleStartup())
        {
            CheckOnClick = false,
        };

        _installHidHideMenuItem = new ToolStripMenuItem("Install HidHide...", null, (_, _) => onInstallHidHide());

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Opening += OnContextMenuOpening;
        _contextMenu.Items.Add(new ToolStripMenuItem("Open Settings", null, (_, _) => onOpenSettings()));
        _contextMenu.Items.Add(new ToolStripMenuItem("Disconnect Controller", null, (_, _) => onDisconnectController()));
        _contextMenu.Items.Add(_installHidHideMenuItem);
        _contextMenu.Items.Add(_startupMenuItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(new ToolStripMenuItem("About", null, (_, _) => onAbout()));
        _contextMenu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => onExit()));

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Xbox Guide Tray",
            Visible = true,
            ContextMenuStrip = _contextMenu,
        };

        _notifyIcon.DoubleClick += (_, _) => onOpenSettings();
    }

    public void UpdateStartupChecked(bool enabled)
    {
        _startupMenuItem.Checked = enabled;
        _startupMenuItem.Text = "Run at Startup";
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
    }

    private void OnContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _installHidHideMenuItem.Visible = !_isHidHideInstalled();
    }

    private static Icon LoadTrayIcon()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Tray.ico");
        if (File.Exists(iconPath))
        {
            return new Icon(iconPath);
        }

        using Bitmap bitmap = new Bitmap(32, 32);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(16, 124, 16));
        using Font font = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
        graphics.DrawString("X", font, Brushes.White, new PointF(7, 4));
        return Icon.FromHandle(bitmap.GetHicon());
    }
}
