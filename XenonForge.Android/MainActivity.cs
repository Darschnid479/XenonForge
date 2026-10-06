using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using XenonForge.Engine;
using XenonForge.Services;

namespace XenonForge.AndroidApp;

[Activity(
    Label = "XenonForge",
    MainLauncher = true,
    Exported = true,
    Theme = "@android:style/Theme.Material.NoActionBar")]
public sealed class MainActivity : Activity
{
    const int PickFileRequest = 1001;

    TextView _status = null!;
    TextView _details = null!;
    ProgressBar _progress = null!;
    Button _pickButton = null!;
    Button _godButton = null!;
    Button _xexButton = null!;
    Button _cancelButton = null!;

    string? _isoPath;
    XboxTitleInfo? _titleInfo;
    CancellationTokenSource? _cts;
    bool _busy;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(BuildUi());
        SetStatus("Choose an Xbox ISO or ZIP archive.");
    }

    View BuildUi()
    {
        var scroll = new ScrollView(this);
        scroll.SetBackgroundColor(Color.Rgb(9, 14, 25));

        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        root.SetPadding(Dp(20), Dp(26), Dp(20), Dp(26));
        scroll.AddView(root);

        var brand = new TextView(this)
        {
            Text = "XENONFORGE",
            TextSize = 28f
        };
        brand.SetTextColor(Color.Rgb(64, 224, 208));
        brand.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
        root.AddView(brand);

        var subtitle = new TextView(this)
        {
            Text = "Xbox ISO → GOD / XEX Folder • Android Edition",
            TextSize = 14f
        };
        subtitle.SetTextColor(Color.Rgb(170, 184, 205));
        root.AddView(subtitle, Margin(top: 4, bottom: 24));

        _pickButton = MakeButton("SELECT ISO / ZIP");
        _pickButton.Click += (_, _) => PickInput();
        root.AddView(_pickButton, Margin(bottom: 12));

        _details = new TextView(this)
        {
            Text = "No game selected.",
            TextSize = 15f
        };
        _details.SetTextColor(Color.White);
        _details.SetPadding(Dp(14), Dp(14), Dp(14), Dp(14));
        _details.SetBackgroundColor(Color.Rgb(20, 29, 49));
        root.AddView(_details, Margin(bottom: 18));

        _godButton = MakeButton("CONVERT TO GOD");
        _godButton.Enabled = false;
        _godButton.Click += async (_, _) => await ConvertGodAsync();
        root.AddView(_godButton, Margin(bottom: 10));

        _xexButton = MakeButton("EXTRACT XEX FOLDER");
        _xexButton.Enabled = false;
        _xexButton.Click += async (_, _) => await ExtractXexAsync();
        root.AddView(_xexButton, Margin(bottom: 10));

        _cancelButton = MakeButton("CANCEL");
        _cancelButton.Enabled = false;
        _cancelButton.Click += (_, _) => _cts?.Cancel();
        root.AddView(_cancelButton, Margin(bottom: 20));

        _progress = new ProgressBar(this, null, Android.Resource.Attribute.ProgressBarStyleHorizontal)
        {
            Max = 100,
            Progress = 0
        };
        root.AddView(_progress, Margin(bottom: 10));

        _status = new TextView(this)
        {
            TextSize = 13f
        };
        _status.SetTextColor(Color.Rgb(170, 184, 205));
        root.AddView(_status);

        var storage = new TextView(this)
        {
            Text = "\nOutput is stored in XenonForge's Android app documents folder. Large ISO conversion requires substantial free storage.",
            TextSize = 12f
        };
        storage.SetTextColor(Color.Rgb(120, 138, 165));
        root.AddView(storage);

        return scroll;
    }

    Button MakeButton(string text)
    {
        var button = new Button(this)
        {
            Text = text,
            TextSize = 14f
        };
        button.SetTextColor(Color.White);
        return button;
    }

    LinearLayout.LayoutParams Margin(int left = 0, int top = 0, int right = 0, int bottom = 0)
    {
        var p = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent);
        p.SetMargins(Dp(left), Dp(top), Dp(right), Dp(bottom));
        return p;
    }

    int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);

    void PickInput()
    {
        if (_busy) return;

        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        intent.PutExtra(Intent.ExtraMimeTypes, new[]
        {
            "application/zip",
            "application/x-zip-compressed",
            "application/octet-stream"
        });
        StartActivityForResult(intent, PickFileRequest);
    }

    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickFileRequest || resultCode != Result.Ok || data?.Data is null)
            return;

        try
        {
            SetBusy(true);
            SetStatus("Importing selected file...");
            _progress.Progress = 2;

            var imported = await CopyUriToImportsAsync(data.Data);
            var ext = Path.GetExtension(imported);

            if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("Extracting ISO from ZIP...");
                var zipper = new ZipImportService();
                var zipResult = await zipper.ExtractIsosAndDeleteZipAsync(imported, CancellationToken.None);
                if (zipResult.ExtractedIsoPaths.Count == 0)
                    throw new InvalidDataException("No ISO was found in the ZIP archive.");

                _isoPath = zipResult.ExtractedIsoPaths[0];
            }
            else if (ext.Equals(".iso", StringComparison.OrdinalIgnoreCase))
            {
                _isoPath = imported;
            }
            else
            {
                throw new InvalidDataException("Choose a .iso or .zip file.");
            }

            SetStatus("Reading Xbox metadata...");
            _progress.Progress = 8;
            _titleInfo = await Task.Run(() => XboxExecutableParser.ReadTitleInfo(_isoPath));

            _details.Text =
                $"{_titleInfo.DisplayName}\n" +
                $"Title ID: {_titleInfo.TitleIdHex}\n" +
                $"Media ID: {_titleInfo.MediaIdHex}\n" +
                $"Disc: {Math.Max(1, _titleInfo.DiscNumber)}/{Math.Max(1, _titleInfo.DiscCount)}\n" +
                $"Format: {_titleInfo.DiscKind}\n" +
                $"ISO: {FormatBytes(_titleInfo.IsoSize)}";

            _godButton.Enabled = true;
            _xexButton.Enabled = true;
            _progress.Progress = 0;
            SetStatus("Ready.");
        }
        catch (Exception ex)
        {
            _titleInfo = null;
            _isoPath = null;
            _details.Text = "No valid game loaded.";
            ShowError(ex.Message);
            SetStatus("Import failed.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    async Task<string> CopyUriToImportsAsync(Android.Net.Uri uri)
    {
        var imports = Path.Combine(CacheDir!.AbsolutePath, "imports");
        Directory.CreateDirectory(imports);

        var name = QueryDisplayName(uri);
        if (string.IsNullOrWhiteSpace(name))
            name = "import-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ".iso";

        name = SanitizeFileName(name);
        var target = GetUniquePath(imports, name);

        await using var input = ContentResolver!.OpenInputStream(uri)
            ?? throw new IOException("Android could not open the selected file.");
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);

        await input.CopyToAsync(output);
        await output.FlushAsync();

        return target;
    }

    string QueryDisplayName(Android.Net.Uri uri)
    {
        using var cursor = ContentResolver!.Query(uri, null, null, null, null);
        if (cursor is null || !cursor.MoveToFirst()) return string.Empty;

        var index = cursor.GetColumnIndex(OpenableColumns.DisplayName);
        return index >= 0 ? cursor.GetString(index) ?? string.Empty : string.Empty;
    }

    async Task ConvertGodAsync()
    {
        if (_busy || _titleInfo is null) return;

        _cts = new CancellationTokenSource();
        SetBusy(true);
        _cancelButton.Enabled = true;

        try
        {
            var outputRoot = GetOutputRoot();
            var converter = new GodConverter();
            var progress = new Progress<ConversionProgress>(p =>
            {
                _progress.Progress = p.Percent;
                SetStatus($"{p.Stage} — {p.Percent}%");
            });

            var result = await converter.ConvertAsync(
                _titleInfo,
                outputRoot,
                GodTrimMode.SmartTrim,
                progress,
                _cts.Token);

            _progress.Progress = 100;
            SetStatus("GOD conversion complete.");
            ShowInfo($"GOD package created:\n{result.OutputTitleFolder}");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SetStatus("GOD conversion failed.");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    async Task ExtractXexAsync()
    {
        if (_busy || _titleInfo is null) return;

        _cts = new CancellationTokenSource();
        SetBusy(true);
        _cancelButton.Enabled = true;

        try
        {
            var outputRoot = GetOutputRoot();
            var extractor = new XexExtractor();
            var progress = new Progress<XexExtractionProgress>(p =>
            {
                _progress.Progress = p.Percent;
                SetStatus($"Extracting {p.CurrentPath} — {p.Percent}%");
            });

            var result = await extractor.ExtractAsync(
                _titleInfo,
                outputRoot,
                progress,
                _cts.Token);

            _progress.Progress = 100;
            SetStatus("XEX extraction complete.");
            ShowInfo($"Game folder created:\n{result.OutputFolder}\n\nExecutable:\n{result.ExecutablePath}");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SetStatus("XEX extraction failed.");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    string GetOutputRoot()
    {
        var baseDir = GetExternalFilesDir(Android.OS.Environment.DirectoryDocuments)
            ?? FilesDir
            ?? throw new IOException("Android storage is unavailable.");

        var root = Path.Combine(baseDir.AbsolutePath, "XenonForge");
        Directory.CreateDirectory(root);
        return root;
    }

    void SetBusy(bool value)
    {
        _busy = value;
        _pickButton.Enabled = !value;
        _godButton.Enabled = !value && _titleInfo is not null;
        _xexButton.Enabled = !value && _titleInfo is not null;
        _cancelButton.Enabled = value && _cts is not null;
    }

    void SetStatus(string text)
    {
        RunOnUiThread(() => _status.Text = text);
    }

    void ShowError(string message)
    {
        RunOnUiThread(() =>
            new AlertDialog.Builder(this)
                .SetTitle("XenonForge")
                .SetMessage(message)
                .SetPositiveButton("OK", (_, _) => { })
                .Show());
    }

    void ShowInfo(string message)
    {
        RunOnUiThread(() =>
            new AlertDialog.Builder(this)
                .SetTitle("XenonForge")
                .SetMessage(message)
                .SetPositiveButton("OK", (_, _) => { })
                .Show());
    }

    static string FormatBytes(long value)
    {
        if (value >= 1024L * 1024 * 1024) return $"{value / 1024d / 1024d / 1024d:0.00} GB";
        if (value >= 1024L * 1024) return $"{value / 1024d / 1024d:0.0} MB";
        return $"{value / 1024d:0} KB";
    }

    static string SanitizeFileName(string value)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
            value = value.Replace(ch, '_');
        return value;
    }

    static string GetUniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate)) return candidate;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; i < 10000; i++)
        {
            candidate = Path.Combine(directory, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }

        throw new IOException("Could not create a unique import filename.");
    }

    protected override void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnDestroy();
    }
}
