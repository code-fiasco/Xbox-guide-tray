namespace XboxGuideTray.UI;

internal sealed class DimOverlayForm : Form
{
    private readonly Action _onDismiss;

    public DimOverlayForm(Action onDismiss)
    {
        _onDismiss = onDismiss;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        Opacity = 0.45;
        Bounds = SystemInformation.VirtualScreen;
        KeyPreview = true;
        Cursor = Cursors.Default;

        MouseClick += (_, _) => Dismiss();
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Dismiss();
            e.Handled = true;
        }
    }

    private void Dismiss() => _onDismiss();

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            return cp;
        }
    }
}
