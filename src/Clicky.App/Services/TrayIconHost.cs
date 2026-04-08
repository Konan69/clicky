using System.Drawing;
using System.Windows.Forms;

namespace Clicky.App.Services;

public sealed class TrayIconHost : IDisposable
{
    private readonly NotifyIcon notifyIcon;
    private readonly ContextMenuStrip contextMenuStrip;

    public TrayIconHost()
    {
        contextMenuStrip = new ContextMenuStrip();

        var openClickyMenuItem = new ToolStripMenuItem("Open Clicky");
        openClickyMenuItem.Click += HandleToggleMenuItemClick;

        var quitClickyMenuItem = new ToolStripMenuItem("Quit");
        quitClickyMenuItem.Click += HandleExitMenuItemClick;

        contextMenuStrip.Items.Add(openClickyMenuItem);
        contextMenuStrip.Items.Add(new ToolStripSeparator());
        contextMenuStrip.Items.Add(quitClickyMenuItem);

        notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Clicky",
            Visible = true,
            ContextMenuStrip = contextMenuStrip
        };

        notifyIcon.MouseUp += HandleNotifyIconMouseUp;
    }

    public event EventHandler? TogglePanelRequested;

    public event EventHandler? ExitRequested;

    public void UpdateStatusText(string statusText)
    {
        var truncatedStatusText = statusText.Length > 45
            ? statusText[..45]
            : statusText;

        notifyIcon.Text = $"Clicky - {truncatedStatusText}";
    }

    public void Dispose()
    {
        notifyIcon.MouseUp -= HandleNotifyIconMouseUp;
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        contextMenuStrip.Dispose();
    }

    private void HandleNotifyIconMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            TogglePanelRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void HandleToggleMenuItemClick(object? sender, EventArgs e)
    {
        TogglePanelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HandleExitMenuItemClick(object? sender, EventArgs e)
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}

