using XboxGuideTray.Services;

namespace XboxGuideTray.UI;

public sealed class HidHideInstallForm : Form
{
    private readonly HidHideInstallerService _installerService = new();
    private readonly Button _installButton;
    private bool _installInProgress;

    public HidHideInstallForm()
    {
        Text = "Install HidHide";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(440, 200);
        BackColor = Color.FromArgb(245, 245, 245);
        Font = new Font("Segoe UI", 9F);

        Label explanation = new()
        {
            Text =
                "HidHide stops your controller from sending input to background apps while the power menu is open, " +
                "so only Xbox Guide Tray receives button presses.",
            Location = new Point(20, 20),
            Size = new Size(400, 60),
        };

        LinkLabel learnMore = new()
        {
            Text = "Learn more about HidHide",
            Location = new Point(20, 88),
            AutoSize = true,
            LinkColor = Color.FromArgb(0, 102, 204),
        };
        learnMore.LinkClicked += (_, _) =>
            UiHelpers.OpenUrl(Models.AppMetadata.HidHideProjectUrl);

        _installButton = new Button
        {
            Text = "Install HidHide",
            Size = new Size(120, 32),
            Location = new Point(188, 130),
        };
        _installButton.Click += async (_, _) => await InstallAsync();

        Button cancelButton = new()
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Size = new Size(90, 32),
            Location = new Point(320, 130),
        };

        Controls.Add(explanation);
        Controls.Add(learnMore);
        Controls.Add(_installButton);
        Controls.Add(cancelButton);
        CancelButton = cancelButton;
    }

    private async Task InstallAsync()
    {
        if (_installInProgress)
        {
            return;
        }

        _installInProgress = true;
        _installButton.Enabled = false;
        _installButton.Text = "Installing...";

        try
        {
            HidHideInstallResult result = await _installerService.InstallAsync();
            switch (result)
            {
                case HidHideInstallResult.Installed:
                    MessageBox.Show(
                        "HidHide was installed successfully.",
                        "Xbox Guide Tray",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    DialogResult = DialogResult.OK;
                    Close();
                    break;

                case HidHideInstallResult.InstalledRebootRequired:
                    MessageBox.Show(
                        "HidHide was installed. Restart your PC before using power menu input isolation.",
                        "Xbox Guide Tray",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    DialogResult = DialogResult.OK;
                    Close();
                    break;

                case HidHideInstallResult.AlreadyInstalled:
                    MessageBox.Show(
                        "HidHide is already installed.",
                        "Xbox Guide Tray",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    DialogResult = DialogResult.OK;
                    Close();
                    break;

                case HidHideInstallResult.Cancelled:
                    break;

                default:
                    MessageBox.Show(
                        "HidHide could not be installed. Try again later or install it from the Xbox Guide Tray setup program.",
                        "Xbox Guide Tray",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    break;
            }
        }
        finally
        {
            _installInProgress = false;
            _installButton.Enabled = true;
            _installButton.Text = "Install HidHide";
        }
    }
}
