namespace RigGlow;

/// <summary>Shown once, the first time RigGlow runs.</summary>
public sealed class NoticeForm : Form
{
    public NoticeForm()
    {
        Text = "Welcome to " + AppInfo.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Fg;
        Font = Theme.Body;
        ClientSize = new Size(600, 520);
        Icon = MainForm.AppIcon;

        var col = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(28, 22, 28, 10),
            BackColor = Theme.Bg
        };

        var head = Theme.Row(new LogoBox(52), new Label
        {
            Text = AppInfo.Name,
            Font = Theme.PageTitle,
            ForeColor = Theme.Fg,
            AutoSize = true,
            Margin = new Padding(10, 10, 3, 3)
        });
        col.Controls.Add(head);
        col.Controls.Add(Theme.Note($"by {AppInfo.Company}  ·  version {AppInfo.VersionText}", 540));

        col.Controls.Add(Theme.Note(
            "RigGlow is a free, open-source app that controls your PC's RGB lighting and fan speeds.", 540));
        col.Controls[col.Controls.Count - 1].ForeColor = Theme.Fg;

        col.Controls.Add(Theme.H("Before you start"));
        col.Controls.Add(Theme.Note(
            "•  Close other RGB and fan apps such as SignalRGB, iCUE, Armoury Crate or MSI Center. Two apps controlling the same hardware will fight.\n\n" +
            "•  RigGlow uses two free tools: OpenRGB for lighting and the PawnIO driver for fans and RAM. The Setup check page shows what's missing and links to them.\n\n" +
            "•  Your fans stay under your motherboard's control until you change them. If a temperature can't be read, or the CPU reaches 90°C, every fan RigGlow controls goes to full speed. When RigGlow closes, the fans go back to the motherboard.", 540));

        col.Controls.Add(Theme.H("Please note"));
        col.Controls.Add(Theme.Note(
            "RigGlow talks directly to your hardware. It's provided as-is, without warranty of any kind (see the MIT licence). Use it at your own risk.", 540));

        var ok = Theme.Btn("I understand, continue", primary: true);
        ok.DialogResult = DialogResult.OK;
        var exit = Theme.Btn("Exit");
        exit.DialogResult = DialogResult.Cancel;
        var buttons = Theme.Row(ok, exit);
        buttons.Margin = new Padding(0, 14, 0, 0);
        col.Controls.Add(buttons);

        Controls.Add(col);
        AcceptButton = ok;
        CancelButton = exit;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.UseDarkTitleBar(Handle);
    }
}
