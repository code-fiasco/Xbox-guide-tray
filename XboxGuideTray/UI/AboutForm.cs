using System.Reflection;

namespace XboxGuideTray.UI;

public sealed class AboutForm : Form
{
    public AboutForm()
    {
        Text = "About Xbox Guide Tray";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(420, 260);
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.White;

        Label title = new()
        {
            Text = "Xbox Guide Tray",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 24),
        };

        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        Label details = new()
        {
            Text = $"Version {version}{Environment.NewLine}{Environment.NewLine}Power menu input isolation uses HidHide. Install it from the setup wizard if you skipped it during installation.",
            AutoSize = true,
            Location = new Point(24, 70),
        };

        Button closeButton = new()
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            Size = new Size(90, 32),
            Location = new Point(300, 205),
        };
        closeButton.Click += (_, _) => Close();

        Controls.Add(title);
        Controls.Add(details);
        Controls.Add(closeButton);
        AcceptButton = closeButton;
    }
}
