using MCPHub.Core;

namespace MCPHub.App;

internal sealed class FfmpegPanel : UserControl
{
    private readonly FfmpegService _service;
    private readonly CancellationToken _token;

    private readonly TextBox _inputPath = new() { Width = 480, AccessibleName = "Input file path" };
    private readonly TextBox _outputPath = new() { Width = 480, AccessibleName = "Output file path" };
    private readonly ComboBox _preset = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 200,
        AccessibleName = "Conversion preset"
    };
    private readonly TextBox _output = Ui.LogBox();
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Top, Height = 8, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 0, Visible = false };

    public FfmpegPanel(FfmpegService service, CancellationToken token)
    {
        _service = service;
        _token = token;

        _preset.Items.AddRange([
            "MP4 → H.264 (video)",
            "MKV → MP4 (video)",
            "AVI → MP4 (video)",
            "Extract audio → MP3",
            "Extract audio → AAC",
            "WAV → MP3 (audio)",
            "FLAC → MP3 (audio)",
            "Custom FFmpeg args"
        ]);
        _preset.SelectedIndex = 0;

        var top = Ui.Row();
        top.Controls.Add(Ui.Label("FFmpeg Media Converter", true));
        top.Controls.AddRange([
            Ui.Button("Check &FFmpeg", async (_, _) => await CheckFfmpeg()),
            Ui.Button("&Convert", async (_, _) => await RunConvert()),
            Ui.Button("&Cancel", (_, _) => _service.Cancel())
        ]);

        var inputRow = Ui.Row();
        inputRow.Controls.Add(Ui.Label("Input"));
        inputRow.Controls.Add(_inputPath);
        inputRow.Controls.Add(Ui.Button("&Browse…", (_, _) => BrowseInput()));

        var outputRow = Ui.Row();
        outputRow.Controls.Add(Ui.Label("Output"));
        outputRow.Controls.Add(_outputPath);
        outputRow.Controls.Add(Ui.Button("Save &as…", (_, _) => BrowseOutput()));

        var presetRow = Ui.Row();
        presetRow.Controls.Add(Ui.Label("Preset"));
        presetRow.Controls.Add(_preset);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 240 };
        split.Panel1.Controls.Add(Ui.Group("Conversion log", _output));
        split.Panel2.Controls.Add(Ui.Group("Supported formats", BuildFormatsPanel()));

        Controls.Add(split);
        Controls.Add(_progress);
        Controls.Add(presetRow);
        Controls.Add(outputRow);
        Controls.Add(inputRow);
        Controls.Add(top);
    }

    private Panel BuildFormatsPanel()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        var lbl = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            Text =
                "Video: MP4, MKV, AVI, MOV, WebM, FLV\r\n" +
                "Audio: MP3, AAC, WAV, FLAC, OGG, M4A\r\n" +
                "\r\nRequires ffmpeg.exe on PATH or in app directory.",
            AutoSize = false
        };
        p.Controls.Add(lbl);
        return p;
    }

    private void BrowseInput()
    {
        using var d = new OpenFileDialog
        {
            Title = "Select input media file",
            Filter = "Media files|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.flv;*.mp3;*.aac;*.wav;*.flac;*.ogg;*.m4a|All files|*.*"
        };
        if (d.ShowDialog(this) == DialogResult.OK)
        {
            _inputPath.Text = d.FileName;
            if (string.IsNullOrWhiteSpace(_outputPath.Text))
                _outputPath.Text = Path.ChangeExtension(d.FileName, SuggestExtension());
        }
    }

    private void BrowseOutput()
    {
        using var d = new SaveFileDialog
        {
            Title = "Choose output file",
            Filter = "MP4|*.mp4|MP3|*.mp3|MKV|*.mkv|AAC|*.aac|WAV|*.wav|All files|*.*",
            FileName = Path.GetFileNameWithoutExtension(_inputPath.Text)
        };
        if (d.ShowDialog(this) == DialogResult.OK)
            _outputPath.Text = d.FileName;
    }

    private string SuggestExtension() => _preset.SelectedIndex switch
    {
        0 or 1 or 2 => ".mp4",
        3 or 6 => ".mp3",
        4 => ".aac",
        5 => ".mp3",
        _ => ".mp4"
    };

    private async Task CheckFfmpeg()
    {
        try
        {
            var version = await _service.GetVersionAsync(_token);
            _output.Text = version;
        }
        catch (Exception e) { Ui.Error(this, e); _output.Text = e.Message; }
    }

    private async Task RunConvert()
    {
        var input = _inputPath.Text.Trim();
        var output = _outputPath.Text.Trim();
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
        {
            MessageBox.Show(this, "Please specify both input and output paths.", "MCPHub", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _progress.MarqueeAnimationSpeed = 30;
        _progress.Visible = true;
        _output.Text = string.Empty;

        try
        {
            var args = BuildArgs(input, output);
            await foreach (var line in _service.ConvertAsync(input, args, _token))
                _output.AppendText(line + Environment.NewLine);

            _output.AppendText("\r\n✓ Done.");
        }
        catch (OperationCanceledException) { _output.AppendText("\r\nCancelled."); }
        catch (Exception e) { Ui.Error(this, e); _output.AppendText("\r\nError: " + e.Message); }
        finally { _progress.MarqueeAnimationSpeed = 0; _progress.Visible = false; }
    }

    private string BuildArgs(string input, string output) => _preset.SelectedIndex switch
    {
        0 => $"-i \"{input}\" -c:v libx264 -crf 23 -preset fast -c:a aac \"{output}\"",
        1 => $"-i \"{input}\" -c:v libx264 -crf 22 -c:a copy \"{output}\"",
        2 => $"-i \"{input}\" -c:v libx264 -c:a aac \"{output}\"",
        3 => $"-i \"{input}\" -vn -q:a 2 \"{output}\"",
        4 => $"-i \"{input}\" -vn -c:a aac -b:a 192k \"{output}\"",
        5 => $"-i \"{input}\" -q:a 2 \"{output}\"",
        6 => $"-i \"{input}\" -q:a 2 \"{output}\"",
        _ => $"-i \"{input}\" \"{output}\""  // custom – user edited output path with args
    };
}
