using XboxGuideTray.Services;

namespace XboxGuideTray.UI;

public sealed class PowerMenuForm : Form
{
    private readonly GuideButtonService _guideButtonService;
    private readonly ControllerInputBlocker _inputBlocker;
    private readonly ulong _bluetoothAddress;
    private readonly Action _turnOffPc;
    private readonly Action _turnOffController;
    private readonly Action _restartPc;
    private readonly System.Windows.Forms.Timer _inputTimer;
    private readonly List<PowerMenuItem> _items;
    private readonly DimOverlayForm _overlay;
    private int _selectedIndex;
    private bool _aWasPressed;
    private bool _bWasPressed;
    private bool _guideWasPressed;
    private bool _dpadUpWasPressed;
    private bool _dpadDownWasPressed;
    private bool _stickUpWasActive;
    private bool _stickDownWasActive;
    // Menu opens while Guide is still held from the long press; ignore that release as dismiss.
    private bool _awaitInitialGuideRelease;
    private bool _isClosing;

    public PowerMenuForm(
        GuideButtonService guideButtonService,
        ControllerInputBlocker inputBlocker,
        ulong bluetoothAddress,
        Action turnOffPc,
        Action turnOffController,
        Action restartPc)
    {
        _guideButtonService = guideButtonService;
        _inputBlocker = inputBlocker;
        _bluetoothAddress = bluetoothAddress;
        _turnOffPc = turnOffPc;
        _turnOffController = turnOffController;
        _restartPc = restartPc;

        _items =
        [
            new PowerMenuItem("Turn Off PC", "\u23FB", _turnOffPc),
            new PowerMenuItem("Turn Off Controller", "\U0001F3AE", _turnOffController),
            new PowerMenuItem("Restart PC", "\u21BB", _restartPc),
        ];

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(28, 28, 30);
        ForeColor = Color.White;
        DoubleBuffered = true;
        KeyPreview = true;

        int screenWidth = Screen.PrimaryScreen?.WorkingArea.Width ?? 1280;
        Width = Math.Max(360, (int)(screenWidth * 0.35));
        Height = 320;
        Padding = new Padding(24);

        _overlay = new DimOverlayForm(CloseMenu);
        KeyDown += OnKeyDown;

        _inputTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _inputTimer.Tick += (_, _) => PollControllerInput();
        Shown += OnShown;
        FormClosed += (_, _) =>
        {
            _inputTimer.Stop();
            _inputBlocker.EndBlock();
            _overlay.Close();
        };

        Paint += OnPaint;
    }

    private void OnShown(object? sender, EventArgs e)
    {
        if (!_inputBlocker.BeginBlock(_bluetoothAddress))
        {
            string reason = _inputBlocker.UnavailableReason ?? "Unknown reason.";
            AppLogger.Warn($"Controller input blocking was not enabled: {reason}");
            MessageBox.Show(
                "Controller input could not be isolated from other apps for this power menu session." +
                $"{Environment.NewLine}{Environment.NewLine}{reason}",
                "Xbox Guide Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        _overlay.Show();
        Activate();
        Focus();

        _awaitInitialGuideRelease = _guideButtonService.IsGuidePressed();
        _guideWasPressed = _awaitInitialGuideRelease;
        _inputTimer.Start();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            CloseMenu();
            e.Handled = true;
        }
    }

    private void CloseMenu()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        Close();
    }

    private void PollControllerInput()
    {
        // Guide uses VK 0x07; D-pad/A/B use XInput (more reliable when another app has focus).
        bool guidePressed = _guideButtonService.IsGuidePressed();

        if (_awaitInitialGuideRelease)
        {
            if (!guidePressed)
            {
                _awaitInitialGuideRelease = false;
                _guideWasPressed = false;
            }
        }
        else if (guidePressed && !_guideWasPressed)
        {
            CloseMenu();
            return;
        }

        _guideWasPressed = guidePressed;

        if (!XInputGamepadReader.TryRead(out GamepadState pad))
        {
            return;
        }

        bool upEdge = (pad.DpadUpPressed && !_dpadUpWasPressed) ||
                      (pad.Vertical < 0 && !_stickUpWasActive);
        bool downEdge = (pad.DpadDownPressed && !_dpadDownWasPressed) ||
                        (pad.Vertical > 0 && !_stickDownWasActive);

        if (upEdge)
        {
            _selectedIndex = (_selectedIndex - 1 + _items.Count) % _items.Count;
            Invalidate();
        }
        else if (downEdge)
        {
            _selectedIndex = (_selectedIndex + 1) % _items.Count;
            Invalidate();
        }

        _dpadUpWasPressed = pad.DpadUpPressed;
        _dpadDownWasPressed = pad.DpadDownPressed;
        _stickUpWasActive = pad.Vertical < 0;
        _stickDownWasActive = pad.Vertical > 0;

        if (pad.APressed && !_aWasPressed)
        {
            ActivateSelectedItem();
            return;
        }

        if (pad.BPressed && !_bWasPressed)
        {
            CloseMenu();
            return;
        }

        _aWasPressed = pad.APressed;
        _bWasPressed = pad.BPressed;
    }

    private void ActivateSelectedItem()
    {
        PowerMenuItem selected = _items[_selectedIndex];
        CloseMenu();
        selected.Action();
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using GraphicsPath path = CreateRoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 18);
        Region = new Region(path);
        using SolidBrush background = new(Color.FromArgb(220, 28, 28, 30));
        g.FillPath(background, path);

        using Font titleFont = new("Segoe UI", 16, FontStyle.Bold);
        g.DrawString("Power Options", titleFont, Brushes.White, 24, 20);

        int y = 80;
        for (int i = 0; i < _items.Count; i++)
        {
            PowerMenuItem item = _items[i];
            Rectangle row = new(20, y, Width - 40, 52);
            if (i == _selectedIndex)
            {
                using SolidBrush selected = new(Color.FromArgb(70, 16, 124, 16));
                using GraphicsPath rowPath = CreateRoundedRect(row, 10);
                g.FillPath(selected, rowPath);
            }

            using Font itemFont = new("Segoe UI", 12, FontStyle.Regular);
            g.DrawString($"{item.Icon}  {item.Text}", itemFont, Brushes.White, row.Left + 14, row.Top + 14);
            y += 60;
        }

        using Font hintFont = new("Segoe UI", 10, FontStyle.Regular);
        DrawCancelHint(g, hintFont);
    }

    private void DrawCancelHint(Graphics g, Font labelFont)
    {
        const int margin = 24;
        const int buttonSize = 22;
        const int gap = 8;

        string label = "cancel";
        SizeF labelSize = g.MeasureString(label, labelFont);

        int totalWidth = buttonSize + gap + (int)Math.Ceiling(labelSize.Width);
        int buttonY = Height - margin - buttonSize;
        int startX = Width - margin - totalWidth;

        Rectangle buttonBounds = new(startX, buttonY, buttonSize, buttonSize);
        using Pen circlePen = new(Color.FromArgb(200, 200, 200), 1.5f);
        g.DrawEllipse(circlePen, buttonBounds);

        using Font buttonFont = new("Segoe UI", 9, FontStyle.Bold);
        Rectangle textBounds = new(
            buttonBounds.X + 1,
            buttonBounds.Y + 1,
            buttonBounds.Width,
            buttonBounds.Height);
        TextRenderer.DrawText(
            g,
            "B",
            buttonFont,
            textBounds,
            Color.Gainsboro,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine);

        float labelY = buttonY + (buttonSize - labelSize.Height) / 2f;
        g.DrawString(label, labelFont, Brushes.Gainsboro, startX + buttonSize + gap, labelY);
    }

    private static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        GraphicsPath path = new();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class PowerMenuItem(string text, string icon, Action action)
    {
        public string Text { get; } = text;
        public string Icon { get; } = icon;
        public Action Action { get; } = action;
    }
}
