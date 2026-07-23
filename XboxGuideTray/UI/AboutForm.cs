using System.Reflection;
using XboxGuideTray.Models;

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
        ClientSize = new Size(440, 280);
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.White;

        Label title = new()
        {
            Text = "Xbox Guide Tray",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 20),
        };

        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        Label versionLabel = new()
        {
            Text = $"Version {version}",
            AutoSize = true,
            Location = new Point(24, 52),
            ForeColor = Color.Gainsboro,
        };

        Label description = new()
        {
            Text =
                "Manages an Xbox controller over Bluetooth, maps the Guide button, and provides a power menu. " +
                "Optional HidHide support blocks controller input from reaching other apps while the power menu is open.",
            Location = new Point(24, 82),
            Size = new Size(392, 56),
        };

        LinkLabel githubLink = CreateLink("GitHub repository", AppMetadata.GitHubUrl, 24, 148);
        LinkLabel hidHideLink = CreateLink("HidHide project", AppMetadata.HidHideProjectUrl, 24, 172);

        string licensePath = Path.Combine(AppContext.BaseDirectory, "LICENSE");
        LinkLabel licenseLink = new()
        {
            Text = "MIT License",
            AutoSize = true,
            Location = new Point(24, 196),
            LinkColor = Color.FromArgb(120, 180, 255),
            VisitedLinkColor = Color.FromArgb(120, 180, 255),
            ActiveLinkColor = Color.White,
        };
        licenseLink.LinkClicked += (_, _) => UiHelpers.OpenLocalFile(licensePath);

        Button closeButton = new()
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            Size = new Size(90, 32),
            Location = new Point(326, 232),
        };
        closeButton.Click += (_, _) => Close();

        Controls.Add(title);
        Controls.Add(versionLabel);
        Controls.Add(description);
        Controls.Add(githubLink);
        Controls.Add(hidHideLink);
        Controls.Add(licenseLink);
        Controls.Add(closeButton);
        AcceptButton = closeButton;
    }

    private static LinkLabel CreateLink(string text, string url, int x, int y)
    {
        LinkLabel link = new()
        {
            Text = text,
            AutoSize = true,
            Location = new Point(x, y),
            LinkColor = Color.FromArgb(120, 180, 255),
            VisitedLinkColor = Color.FromArgb(120, 180, 255),
            ActiveLinkColor = Color.White,
        };
        link.LinkClicked += (_, _) => UiHelpers.OpenUrl(url);
        return link;
    }
}
