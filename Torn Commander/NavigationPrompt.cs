namespace TornCommander;

internal enum NavigationChoice
{
    Block,
    AllowOnce,
    AlwaysAllow,
    OpenExternal
}

internal static class NavigationPrompt
{
    public static NavigationChoice Show(IWin32Window? owner, Uri uri)
    {
        var result = NavigationChoice.Block;
        using var f = new Form
        {
            Text = "Torn Commander — New site",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            Width = 610,
            Height = 255,
            BackColor = Color.FromArgb(25, 28, 34),
            ForeColor = Color.FromArgb(235, 238, 242),
            Font = new Font("Segoe UI", 9.5f)
        };

        var label = new Label
        {
            Left = 18,
            Top = 18,
            Width = 555,
            Height = 100,
            ForeColor = f.ForeColor,
            Text = $"This site is not currently on Torn Commander's navigation list:\n\n{uri.Host}\n\nChoose whether to allow it. 'Always allow' saves the domain to your personal whitelist."
        };
        f.Controls.Add(label);

        Button Add(string text, int x, int width, NavigationChoice choice)
        {
            var b = new Button
            {
                Text = text,
                Left = x,
                Top = 145,
                Width = width,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(35, 39, 47),
                ForeColor = f.ForeColor
            };
            b.Click += (_, _) => { result = choice; f.DialogResult = DialogResult.OK; f.Close(); };
            f.Controls.Add(b);
            return b;
        }

        Add("Allow once", 18, 105, NavigationChoice.AllowOnce);
        Add("Always allow", 132, 115, NavigationChoice.AlwaysAllow);
        Add("Open in Edge", 256, 105, NavigationChoice.OpenExternal);
        Add("Block", 370, 90, NavigationChoice.Block);

        var note = new Label
        {
            Left = 18,
            Top = 190,
            Width = 555,
            Height = 38,
            ForeColor = Color.FromArgb(165, 172, 182),
            Text = "This whitelist only controls Torn Commander navigation. It cannot bypass Torn/Cloudflare/Google security checks or CAPTCHAs."
        };
        f.Controls.Add(note);

        f.ShowDialog(owner);
        return result;
    }
}
