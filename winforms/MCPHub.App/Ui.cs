namespace MCPHub.App;

internal static class Ui
{
    public static Button Button(string text, EventHandler? click = null, int width = 100)
    {
        var button = new Button { Text = text, AutoSize = false, Width = width, Height = 30, Margin = new(3), FlatStyle = FlatStyle.System };
        if (click is not null) button.Click += click; return button;
    }
    public static Label Label(string text, bool bold = false) => new() { Text = text, AutoSize = true, Font = new Font(Control.DefaultFont, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new(4, 7, 4, 4) };
    public static TableLayoutPanel Table(int columns = 1) { var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, AutoScroll = true, Padding = new(8) }; for (var i = 0; i < columns; i++) table.ColumnStyles.Add(new(SizeType.Percent, 100f / columns)); return table; }
    public static FlowLayoutPanel Row() => new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new(2) };
    public static TextBox LogBox() => new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f), BackColor = Color.FromArgb(25, 25, 25), ForeColor = Color.Gainsboro };
    public static GroupBox Group(string title, Control child) { var box = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new(8) }; box.Controls.Add(child); return box; }
    public static void Error(IWin32Window owner, Exception error) => MessageBox.Show(owner, error.Message, "MCPHub", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
