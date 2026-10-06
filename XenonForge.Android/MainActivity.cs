using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using System.Net.Http;
using System.Text.Json;
using XenonForge.Engine;
using XenonForge.Services;
using Color = Android.Graphics.Color;
using BitmapFactory = Android.Graphics.BitmapFactory;
using IOPath = System.IO.Path;
using OperationCanceledException = System.OperationCanceledException;

namespace XenonForgeMobile;

[Activity(
    Label = "XenonForge",
    MainLauncher = true,
    Exported = true,
    Theme = "@android:style/Theme.Material.NoActionBar")]
public sealed class MainActivity : Activity
{
    const int PickFilesRequest = 1001;
    const int PickFolderRequest = 1002;
    const int PickDeployFolderRequest = 1003;

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    readonly List<GameRow> _queue = new();
    readonly List<GodConversionResult> _completed = new();

    LinearLayout _root = null!;
    LinearLayout _contentHost = null!;
    TextView _status = null!;
    ProgressBar _progress = null!;
    TextView _details = null!;
    ImageView _cover = null!;
    ListView _queueList = null!;
    ArrayAdapter<string>? _queueAdapter;
    CheckBox _smartTrim = null!;
    CheckBox _autoDeploy = null!;
    TextView _deployTargetText = null!;
    TextView _libraryText = null!;
    TextView _activityText = null!;

    CancellationTokenSource? _cts;
    bool _busy;
    Android.Net.Uri? _deployTreeUri;
    int _selectedQueueIndex = -1;

    string OutputRoot
    {
        get
        {
            var baseDir = GetExternalFilesDir(Android.OS.Environment.DirectoryDocuments)
                ?? FilesDir
                ?? throw new IOException("Android storage is unavailable.");
            var root = IOPath.Combine(baseDir.AbsolutePath, "XenonForge");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    string CoverCacheRoot
    {
        get
        {
            var root = IOPath.Combine(CacheDir!.AbsolutePath, "covers");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        LoadSettings();
        SetContentView(BuildShell());
        ShowConvertPage();
        SetStatus("Ready. Add an ISO/ZIP or folder.");
    }

    View BuildShell()
    {
        var outer = new LinearLayout(this) { Orientation = Orientation.Vertical };
        outer.SetBackgroundColor(Color.Rgb(9, 14, 25));

        var header = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            Gravity = GravityFlags.CenterVertical
        };
        header.SetPadding(Dp(16), Dp(14), Dp(16), Dp(8));

        var brand = new TextView(this) { Text = "XF", TextSize = 19f, Gravity = GravityFlags.Center };
        brand.SetTypeface(null, global::Android.Graphics.TypefaceStyle.Bold);
        brand.SetTextColor(Color.Rgb(9, 14, 25));
        brand.SetBackgroundColor(Color.Rgb(64, 224, 208));
        header.AddView(brand, new LinearLayout.LayoutParams(Dp(48), Dp(48)));

        var titles = new LinearLayout(this) { Orientation = Orientation.Vertical };
        titles.SetPadding(Dp(12), 0, 0, 0);
        var title = new TextView(this) { Text = "XENONFORGE", TextSize = 20f };
        title.SetTypeface(null, global::Android.Graphics.TypefaceStyle.Bold);
        title.SetTextColor(Color.White);
        var sub = new TextView(this) { Text = "GOD STUDIO • ANDROID", TextSize = 10f };
        sub.SetTextColor(Color.Rgb(135, 153, 180));
        titles.AddView(title);
        titles.AddView(sub);
        header.AddView(titles, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        outer.AddView(header);

        var navScroll = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        var nav = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        nav.SetPadding(Dp(10), Dp(6), Dp(10), Dp(10));
        nav.AddView(NavButton("●  CONVERT", ShowConvertPage));
        nav.AddView(NavButton("▣  USB DEPLOY", ShowUsbPage));
        nav.AddView(NavButton("◫  LIBRARY", ShowLibraryPage));
        nav.AddView(NavButton("⚙  SETTINGS", ShowSettingsPage));
        navScroll.AddView(nav);
        outer.AddView(navScroll);

        _contentHost = new LinearLayout(this) { Orientation = Orientation.Vertical };
        outer.AddView(_contentHost, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, 1f));

        var footer = new LinearLayout(this) { Orientation = Orientation.Vertical };
        footer.SetPadding(Dp(16), Dp(8), Dp(16), Dp(12));

        _progress = new ProgressBar(this, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal)
        {
            Max = 100,
            Progress = 0
        };
        footer.AddView(_progress);

        _status = new TextView(this) { TextSize = 12f };
        _status.SetTextColor(Color.Rgb(150, 167, 194));
        footer.AddView(_status, Margin(top: 6));

        outer.AddView(footer);
        _root = outer;
        return outer;
    }

    Button NavButton(string text, Action action)
    {
        var b = MakeButton(text);
        b.Click += (_, _) => { if (!_busy) action(); };
        var p = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(42));
        p.SetMargins(Dp(4), 0, Dp(4), 0);
        return ApplyLayout(b, p);
    }

    void ShowConvertPage()
    {
        _contentHost.RemoveAllViews();
        var scroll = new ScrollView(this);
        var page = PageColumn();
        scroll.AddView(page);

        var headline = Heading("Turn clean ISOs into dashboard-ready GOD packages.");
        page.AddView(headline);
        page.AddView(Muted("Identify the game • convert natively • deploy straight to storage"), Margin(bottom: 14));

        var actions = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var add = MakeButton("+ ADD ISO/ZIP");
        add.Click += (_, _) => PickFiles();
        actions.AddView(add, new LinearLayout.LayoutParams(0, Dp(46), 1f));

        var addFolder = MakeButton("ADD FOLDER");
        addFolder.Click += (_, _) => PickFolder();
        var afp = new LinearLayout.LayoutParams(0, Dp(46), 1f);
        afp.SetMargins(Dp(8), 0, 0, 0);
        actions.AddView(addFolder, afp);
        page.AddView(actions, Margin(bottom: 10));

        var actions2 = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var god = MakeButton("CONVERT GOD");
        god.Click += async (_, _) => await ConvertQueueAsync(false);
        actions2.AddView(god, new LinearLayout.LayoutParams(0, Dp(46), 1f));

        var xex = MakeButton("EXTRACT XEX");
        xex.Click += async (_, _) => await ExtractQueueAsync();
        var xp = new LinearLayout.LayoutParams(0, Dp(46), 1f);
        xp.SetMargins(Dp(8), 0, 0, 0);
        actions2.AddView(xex, xp);
        page.AddView(actions2, Margin(bottom: 10));

        var actions3 = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var godUsb = MakeButton("GOD + USB");
        godUsb.Click += async (_, _) => await ConvertQueueAsync(true);
        actions3.AddView(godUsb, new LinearLayout.LayoutParams(0, Dp(46), 1f));

        var cancel = MakeButton("CANCEL");
        cancel.Click += (_, _) => _cts?.Cancel();
        var cp = new LinearLayout.LayoutParams(0, Dp(46), 1f);
        cp.SetMargins(Dp(8), 0, 0, 0);
        actions3.AddView(cancel, cp);
        page.AddView(actions3, Margin(bottom: 16));

        page.AddView(SectionTitle("CONVERSION QUEUE"));
        page.AddView(Muted("ISO and ZIP files are scanned automatically."), Margin(bottom: 8));

        _queueList = new ListView(this);
        RefreshQueueAdapter();
        _queueList.ItemClick += async (_, e) =>
        {
            _selectedQueueIndex = e.Position;
            await UpdateSelectedGameAsync();
        };
        page.AddView(_queueList, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Dp(220)));

        page.AddView(SectionTitle("GAME DETAILS"), Margin(top: 14));
        var detailsRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _cover = new ImageView(this);
        _cover.SetBackgroundColor(Color.Rgb(12, 19, 32));
        _cover.SetScaleType(ImageView.ScaleType.CenterCrop);
        detailsRow.AddView(_cover, new LinearLayout.LayoutParams(Dp(112), Dp(158)));

        _details = new TextView(this)
        {
            Text = "Select a game",
            TextSize = 14f
        };
        _details.SetTextColor(Color.White);
        _details.SetPadding(Dp(14), Dp(4), 0, 0);
        detailsRow.AddView(_details, new LinearLayout.LayoutParams(0, Dp(158), 1f));
        page.AddView(detailsRow);

        _smartTrim = new CheckBox(this) { Text = "Smart trim unused tail space" };
        _smartTrim.Checked = GetPrefs().GetBoolean("smart_trim", true);
        _smartTrim.SetTextColor(Color.White);
        _smartTrim.CheckedChange += (_, e) =>
        {
            GetPrefs().Edit()!.PutBoolean("smart_trim", e.IsChecked).Apply();
        };
        page.AddView(_smartTrim, Margin(top: 10));

        page.AddView(SectionTitle("ACTIVITY"), Margin(top: 12));
        _activityText = new TextView(this) { TextSize = 11f };
        _activityText.SetTextColor(Color.Rgb(170, 184, 205));
        _activityText.SetBackgroundColor(Color.Rgb(10, 15, 27));
        _activityText.SetPadding(Dp(10), Dp(10), Dp(10), Dp(10));
        page.AddView(_activityText, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Dp(150)));

        _contentHost.AddView(scroll, Match());
        if (_selectedQueueIndex >= 0 && _selectedQueueIndex < _queue.Count)
            _ = UpdateSelectedGameAsync();
    }

    void ShowUsbPage()
    {
        _contentHost.RemoveAllViews();
        var scroll = new ScrollView(this);
        var page = PageColumn();
        scroll.AddView(page);

        page.AddView(Heading("USB DEPLOY"));
        page.AddView(Muted("Android uses the system folder picker for USB-OTG, SD cards and shared storage."), Margin(bottom: 16));

        _deployTargetText = new TextView(this)
        {
            Text = _deployTreeUri is null ? "No destination selected." : $"Destination: {_deployTreeUri}",
            TextSize = 13f
        };
        _deployTargetText.SetTextColor(Color.White);
        _deployTargetText.SetBackgroundColor(Color.Rgb(20, 29, 49));
        _deployTargetText.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        page.AddView(_deployTargetText, Margin(bottom: 10));

        var choose = MakeButton("SELECT USB / DESTINATION");
        choose.Click += (_, _) => PickDeployFolder();
        page.AddView(choose, Margin(bottom: 10));

        var deploy = MakeButton("DEPLOY SELECTED / LATEST");
        deploy.Click += async (_, _) => await DeploySelectedAsync();
        page.AddView(deploy, Margin(bottom: 16));

        page.AddView(SectionTitle("COMPLETED CONVERSIONS"));
        var completedText = new TextView(this) { TextSize = 13f };
        completedText.SetTextColor(Color.Rgb(190, 203, 222));
        completedText.Text = _completed.Count == 0
            ? "No completed GOD conversions in this session."
            : string.Join("\n\n", _completed.Select((x, i) =>
                $"{i + 1}. {x.Info.DisplayName}\n{x.Info.TitleIdHex} • {FormatBytes(x.TotalBytes)}"));
        page.AddView(completedText);

        _contentHost.AddView(scroll, Match());
    }

    void ShowLibraryPage()
    {
        _contentHost.RemoveAllViews();
        var scroll = new ScrollView(this);
        var page = PageColumn();
        scroll.AddView(page);

        page.AddView(Heading("LIBRARY"));
        page.AddView(Muted($"Output root: {OutputRoot}"), Margin(bottom: 12));

        var refresh = MakeButton("REFRESH LIBRARY");
        refresh.Click += (_, _) => RefreshLibrary();
        page.AddView(refresh, Margin(bottom: 12));

        _libraryText = new TextView(this) { TextSize = 13f };
        _libraryText.SetTextColor(Color.Rgb(190, 203, 222));
        _libraryText.SetBackgroundColor(Color.Rgb(15, 22, 38));
        _libraryText.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        page.AddView(_libraryText);

        _contentHost.AddView(scroll, Match());
        RefreshLibrary();
    }

    void ShowSettingsPage()
    {
        _contentHost.RemoveAllViews();
        var scroll = new ScrollView(this);
        var page = PageColumn();
        scroll.AddView(page);

        page.AddView(Heading("SETTINGS"));
        page.AddView(Muted("Conversion and Android storage preferences."), Margin(bottom: 16));

        var trim = new CheckBox(this)
        {
            Text = "Smart trim unused tail space",
            Checked = GetPrefs().GetBoolean("smart_trim", true)
        };
        trim.SetTextColor(Color.White);
        trim.CheckedChange += (_, e) => GetPrefs().Edit()!.PutBoolean("smart_trim", e.IsChecked).Apply();
        page.AddView(trim);

        _autoDeploy = new CheckBox(this)
        {
            Text = "Auto deploy after GOD conversion",
            Checked = GetPrefs().GetBoolean("auto_deploy", false)
        };
        _autoDeploy.SetTextColor(Color.White);
        _autoDeploy.CheckedChange += (_, e) => GetPrefs().Edit()!.PutBoolean("auto_deploy", e.IsChecked).Apply();
        page.AddView(_autoDeploy);

        page.AddView(SectionTitle("OUTPUT"), Margin(top: 12));
        page.AddView(Muted(OutputRoot), Margin(bottom: 12));

        var target = MakeButton("SELECT USB / DESTINATION");
        target.Click += (_, _) => PickDeployFolder();
        page.AddView(target, Margin(bottom: 10));

        var clear = MakeButton("CLEAR COVER CACHE");
        clear.Click += (_, _) =>
        {
            try { if (Directory.Exists(CoverCacheRoot)) Directory.Delete(CoverCacheRoot, true); }
            catch { }
            Toast.MakeText(this, "Cover cache cleared.", ToastLength.Short)?.Show();
        };
        page.AddView(clear, Margin(bottom: 10));

        page.AddView(SectionTitle("ENGINE"), Margin(top: 14));
        page.AddView(Muted("Native XenonForge core\nXGD1 / XGD2 / XGD3 / XSF\nGOD + XEX/XBE extraction\nZIP → ISO import"));

        _contentHost.AddView(scroll, Match());
    }

    void PickFiles()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        intent.PutExtra(Intent.ExtraAllowMultiple, true);
        intent.PutExtra(Intent.ExtraMimeTypes, new[]
        {
            "application/zip",
            "application/x-zip-compressed",
            "application/octet-stream"
        });
        StartActivityForResult(intent, PickFilesRequest);
    }

    void PickFolder()
    {
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission |
                        ActivityFlags.GrantPersistableUriPermission);
        StartActivityForResult(intent, PickFolderRequest);
    }

    void PickDeployFolder()
    {
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission |
                        ActivityFlags.GrantWriteUriPermission |
                        ActivityFlags.GrantPersistableUriPermission);
        StartActivityForResult(intent, PickDeployFolderRequest);
    }

    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (resultCode != Result.Ok || data is null) return;

        try
        {
            if (requestCode == PickFilesRequest)
            {
                var uris = new List<Android.Net.Uri>();
                if (data.ClipData is not null)
                {
                    for (var i = 0; i < data.ClipData.ItemCount; i++)
                        if (data.ClipData.GetItemAt(i)?.Uri is Android.Net.Uri uri) uris.Add(uri);
                }
                else if (data.Data is Android.Net.Uri single)
                {
                    uris.Add(single);
                }

                await ImportUrisAsync(uris);
            }
            else if (requestCode == PickFolderRequest && data.Data is Android.Net.Uri folder)
            {
                var flags = data.Flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
                try { ContentResolver!.TakePersistableUriPermission(folder, flags); } catch { }
                await ImportTreeAsync(folder);
            }
            else if (requestCode == PickDeployFolderRequest && data.Data is Android.Net.Uri deploy)
            {
                var flags = data.Flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
                try { ContentResolver!.TakePersistableUriPermission(deploy, flags); } catch { }
                _deployTreeUri = deploy;
                GetPrefs().Edit()!.PutString("deploy_uri", deploy.ToString()).Apply();
                SetStatus("USB / destination selected.");
                if (_deployTargetText is not null)
                    _deployTargetText.Text = $"Destination: {deploy}";
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    async Task ImportUrisAsync(IEnumerable<Android.Net.Uri> uris)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            foreach (var uri in uris)
            {
                SetStatus("Importing file...");
                var local = await CopyUriToImportsAsync(uri);
                await AddLocalInputAsync(local);
            }
        }
        finally
        {
            SetBusy(false);
            RefreshQueueAdapter();
        }
    }

    async Task ImportTreeAsync(Android.Net.Uri treeUri)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var docs = EnumerateTreeFiles(treeUri)
                .Where(x => x.Name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) ||
                            x.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (docs.Count == 0)
                throw new InvalidDataException("No ISO or ZIP files were found in the selected folder.");

            foreach (var doc in docs)
            {
                SetStatus($"Importing {doc.Name}...");
                var local = await CopyUriToImportsAsync(doc.Uri, doc.Name);
                await AddLocalInputAsync(local);
            }
        }
        finally
        {
            SetBusy(false);
            RefreshQueueAdapter();
        }
    }

    async Task AddLocalInputAsync(string path)
    {
        if (IOPath.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            AppendActivity($"ZIP   {IOPath.GetFileName(path)}");
            var zipper = new ZipImportService();
            var result = await zipper.ExtractIsosAndDeleteZipAsync(path, CancellationToken.None);
            foreach (var iso in result.ExtractedIsoPaths)
                await AddIsoAsync(iso);
        }
        else if (IOPath.GetExtension(path).Equals(".iso", StringComparison.OrdinalIgnoreCase))
        {
            await AddIsoAsync(path);
        }
    }

    async Task AddIsoAsync(string isoPath)
    {
        if (_queue.Any(x => string.Equals(x.Info?.IsoPath, isoPath, StringComparison.OrdinalIgnoreCase)))
            return;

        var row = new GameRow { LocalPath = isoPath, Status = "Scanning" };
        _queue.Add(row);
        RefreshQueueAdapter();

        try
        {
            var info = await Task.Run(() => XboxExecutableParser.ReadTitleInfo(isoPath));
            row.Info = info;
            row.Status = "Ready";
            AppendActivity($"SCAN  {info.DisplayName} [{info.TitleIdHex}] {info.DiscKind}");
            if (_selectedQueueIndex < 0) _selectedQueueIndex = _queue.IndexOf(row);
        }
        catch (Exception ex)
        {
            row.Status = "Invalid";
            row.Error = ex.Message;
            AppendActivity($"ERROR {IOPath.GetFileName(isoPath)} — {ex.Message}");
        }
        RefreshQueueAdapter();
    }

    async Task ConvertQueueAsync(bool deployAfter)
    {
        if (_busy) return;
        var ready = _queue.Where(x => x.Info is not null).ToList();
        if (ready.Count == 0) { ShowInfo("Add a valid Xbox ISO first."); return; }

        var shouldDeploy = deployAfter || GetPrefs().GetBoolean("auto_deploy", false);
        if (shouldDeploy && _deployTreeUri is null)
        {
            ShowInfo("Select USB / destination first.");
            ShowUsbPage();
            return;
        }

        _cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            foreach (var row in ready)
            {
                _cts.Token.ThrowIfCancellationRequested();
                row.Status = "Converting";
                RefreshQueueAdapter();

                var converter = new GodConverter();
                var progress = new Progress<ConversionProgress>(p =>
                {
                    _progress.Progress = p.Percent;
                    row.Status = $"{p.Percent}%";
                    SetStatus($"{p.Stage} — {p.Percent}%");
                    RefreshQueueAdapter();
                });

                var result = await converter.ConvertAsync(
                    row.Info!,
                    OutputRoot,
                    GetPrefs().GetBoolean("smart_trim", true) ? GodTrimMode.SmartTrim : GodTrimMode.FullImage,
                    progress,
                    _cts.Token);

                row.Result = result;
                row.Status = "Done";
                _completed.RemoveAll(x => x.Info.TitleId == result.Info.TitleId && x.Info.MediaId == result.Info.MediaId);
                _completed.Add(result);
                AppendActivity($"DONE  {result.Info.DisplayName} → {result.OutputTitleFolder}");

                if (shouldDeploy && _deployTreeUri is not null)
                {
                    row.Status = "USB";
                    await DeployResultToTreeAsync(result, _deployTreeUri, _cts.Token);
                    row.Status = "On USB";
                }
                RefreshQueueAdapter();
            }
            _progress.Progress = 100;
            SetStatus("Queue complete.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled.");
        }
        catch (Exception ex)
        {
            AppendActivity($"FAIL  {ex.Message}");
            ShowError(ex.Message);
            SetStatus("Failed.");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    async Task ExtractQueueAsync()
    {
        if (_busy) return;
        var ready = _queue.Where(x => x.Info is not null).ToList();
        if (ready.Count == 0) { ShowInfo("Add a valid Xbox ISO first."); return; }

        _cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            foreach (var row in ready)
            {
                _cts.Token.ThrowIfCancellationRequested();
                row.Status = "Extracting XEX";
                RefreshQueueAdapter();

                var extractor = new XexExtractor();
                var progress = new Progress<XexExtractionProgress>(p =>
                {
                    _progress.Progress = p.Percent;
                    row.Status = $"XEX {p.Percent}%";
                    SetStatus($"XEX {p.Percent}% — {p.CurrentPath}");
                    RefreshQueueAdapter();
                });

                var result = await extractor.ExtractAsync(row.Info!, OutputRoot, progress, _cts.Token);
                row.Status = "XEX Ready";
                row.XexOutputFolder = result.OutputFolder;
                AppendActivity($"XEXOK {row.Info!.DisplayName} → {result.OutputFolder}");
                RefreshQueueAdapter();
            }
            _progress.Progress = 100;
            SetStatus("XEX extraction complete.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled.");
        }
        catch (Exception ex)
        {
            AppendActivity($"XEXERR {ex.Message}");
            ShowError(ex.Message);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    async Task DeploySelectedAsync()
    {
        if (_busy) return;
        if (_deployTreeUri is null)
        {
            ShowInfo("Select USB / destination first.");
            return;
        }

        GodConversionResult? result = null;
        if (_selectedQueueIndex >= 0 && _selectedQueueIndex < _queue.Count)
            result = _queue[_selectedQueueIndex].Result;
        result ??= _completed.LastOrDefault();

        if (result is null)
        {
            ShowInfo("Convert a GOD package first.");
            return;
        }

        _cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            await DeployResultToTreeAsync(result, _deployTreeUri, _cts.Token);
            SetStatus("USB deploy complete.");
            ShowInfo($"Deployed {result.Info.DisplayName}.");
        }
        catch (OperationCanceledException) { SetStatus("Cancelled."); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    async Task DeployResultToTreeAsync(GodConversionResult result, Android.Net.Uri treeUri, CancellationToken token)
    {
        var rootDoc = DocumentsContract.BuildDocumentUriUsingTree(
            treeUri, DocumentsContract.GetTreeDocumentId(treeUri))
            ?? throw new IOException("Could not open selected destination.");

        var content = EnsureDirectory(rootDoc, "Content");
        var all = EnsureDirectory(content, "0000000000000000");
        var title = EnsureDirectory(all, result.Info.TitleIdHex);

        var files = Directory.EnumerateFiles(result.OutputTitleFolder, "*", SearchOption.AllDirectories).ToList();
        long total = files.Sum(x => new FileInfo(x).Length);
        long copied = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var relative = IOPath.GetRelativePath(result.OutputTitleFolder, file);
            var parts = relative.Split(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar);
            var parent = title;
            for (var i = 0; i < parts.Length - 1; i++)
                parent = EnsureDirectory(parent, parts[i]);

            var target = CreateOrReplaceFile(parent, parts[^1], "application/octet-stream");
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
            await using var output = ContentResolver!.OpenOutputStream(target, "w")
                ?? throw new IOException($"Could not write {parts[^1]}.");

            var buffer = new byte[1024 * 1024];
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var n = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                if (n <= 0) break;
                await output.WriteAsync(buffer.AsMemory(0, n), token);
                copied += n;
                var pct = (int)Math.Min(100L, copied * 100L / Math.Max(1L, total));
                _progress.Progress = pct;
                SetStatus($"USB {pct}% — {parts[^1]}");
            }
        }
    }

    Android.Net.Uri EnsureDirectory(Android.Net.Uri parent, string name)
    {
        var existing = FindChild(parent, name, "vnd.android.document/directory");
        if (existing is not null) return existing;
        return DocumentsContract.CreateDocument(ContentResolver!, parent, "vnd.android.document/directory", name)
            ?? throw new IOException($"Could not create folder {name}.");
    }

    Android.Net.Uri CreateOrReplaceFile(Android.Net.Uri parent, string name, string mime)
    {
        var existing = FindChild(parent, name, null);
        if (existing is not null)
        {
            try { DocumentsContract.DeleteDocument(ContentResolver!, existing); } catch { }
        }
        return DocumentsContract.CreateDocument(ContentResolver!, parent, mime, name)
            ?? throw new IOException($"Could not create file {name}.");
    }

    Android.Net.Uri? FindChild(Android.Net.Uri parent, string name, string? mime)
    {
        var docId = DocumentsContract.GetDocumentId(parent);
        var children = DocumentsContract.BuildChildDocumentsUriUsingTree(parent, docId);
        var projection = new[]
        {
            DocumentsContract.Document.ColumnDocumentId,
            DocumentsContract.Document.ColumnDisplayName,
            DocumentsContract.Document.ColumnMimeType
        };
        using var cursor = ContentResolver!.Query(children, projection, null, null, null);
        if (cursor is null) return null;

        while (cursor.MoveToNext())
        {
            var id = cursor.GetString(0);
            var display = cursor.GetString(1);
            var foundMime = cursor.GetString(2);
            if (!string.Equals(display, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (mime is not null && !string.Equals(foundMime, mime, StringComparison.OrdinalIgnoreCase)) continue;
            return DocumentsContract.BuildDocumentUriUsingTree(parent, id);
        }
        return null;
    }

    List<TreeFile> EnumerateTreeFiles(Android.Net.Uri treeUri)
    {
        var rootId = DocumentsContract.GetTreeDocumentId(treeUri);
        var root = DocumentsContract.BuildDocumentUriUsingTree(treeUri, rootId);
        var result = new List<TreeFile>();
        if (root is not null) WalkTree(root, result, 0);
        return result;
    }

    void WalkTree(Android.Net.Uri parent, List<TreeFile> output, int depth)
    {
        if (depth > 32) return;
        var docId = DocumentsContract.GetDocumentId(parent);
        var children = DocumentsContract.BuildChildDocumentsUriUsingTree(parent, docId);
        var projection = new[]
        {
            DocumentsContract.Document.ColumnDocumentId,
            DocumentsContract.Document.ColumnDisplayName,
            DocumentsContract.Document.ColumnMimeType
        };

        using var cursor = ContentResolver!.Query(children, projection, null, null, null);
        if (cursor is null) return;
        while (cursor.MoveToNext())
        {
            var id = cursor.GetString(0);
            var name = cursor.GetString(1) ?? "file";
            var mime = cursor.GetString(2) ?? "application/octet-stream";
            var uri = DocumentsContract.BuildDocumentUriUsingTree(parent, id);
            if (uri is null) continue;
            if (mime == "vnd.android.document/directory") WalkTree(uri, output, depth + 1);
            else output.Add(new TreeFile(uri, name));
        }
    }

    async Task<string> CopyUriToImportsAsync(Android.Net.Uri uri, string? forcedName = null)
    {
        var imports = IOPath.Combine(CacheDir!.AbsolutePath, "imports");
        Directory.CreateDirectory(imports);

        var name = forcedName ?? QueryDisplayName(uri);
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
        var index = cursor.GetColumnIndex(Android.Provider.IOpenableColumns.DisplayName);
        return index >= 0 ? cursor.GetString(index) ?? string.Empty : string.Empty;
    }

    async Task UpdateSelectedGameAsync()
    {
        if (_selectedQueueIndex < 0 || _selectedQueueIndex >= _queue.Count || _details is null) return;
        var row = _queue[_selectedQueueIndex];
        if (row.Info is null)
        {
            _details.Text = row.Error ?? "Metadata unavailable.";
            return;
        }

        _details.Text =
            $"{row.Info.DisplayName}\n\n" +
            $"Title ID   {row.Info.TitleIdHex}\n" +
            $"Media ID   {row.Info.MediaIdHex}\n" +
            $"Disc       {Math.Max(1, (int)row.Info.DiscNumber)} / {Math.Max(1, (int)row.Info.DiscCount)}\n" +
            $"Format     {row.Info.DiscKind}\n" +
            $"Size       {FormatBytes(row.Info.IsoSize)}";

        await LoadCoverAsync(row.Info.TitleIdHex);
    }

    async Task LoadCoverAsync(string titleId)
    {
        if (_cover is null) return;
        try
        {
            var cache = IOPath.Combine(CoverCacheRoot, titleId + ".cover");
            byte[]? bytes = File.Exists(cache) ? await File.ReadAllBytesAsync(cache) : null;

            if (bytes is null || bytes.Length < 128)
            {
                var json = await Http.GetStringAsync($"http://xboxunity.net/api/Covers/{titleId}");
                using var doc = JsonDocument.Parse(json);
                string? imageUrl = null;
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        foreach (var key in new[] { "front", "thumbnail", "url" })
                        {
                            if (item.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
                            {
                                imageUrl = p.GetString();
                                if (!string.IsNullOrWhiteSpace(imageUrl)) break;
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(imageUrl)) break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(imageUrl))
                {
                    var uri = new Uri(imageUrl);
                    if (uri.Host.EndsWith("xboxunity.net", StringComparison.OrdinalIgnoreCase))
                    {
                        bytes = await Http.GetByteArrayAsync(uri);
                        if (bytes.Length < 12 * 1024 * 1024) await File.WriteAllBytesAsync(cache, bytes);
                    }
                }
            }

            if (bytes is not null && bytes.Length > 128)
            {
                var bmp = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
                RunOnUiThread(() => _cover.SetImageBitmap(bmp));
            }
        }
        catch { }
    }

    void RefreshLibrary()
    {
        if (_libraryText is null) return;
        try
        {
            var lines = new List<string>();
            foreach (var dir in Directory.EnumerateDirectories(OutputRoot))
            {
                var name = IOPath.GetFileName(dir);
                if (name.Equals("XEX", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Length != 8) continue;

                var god = IOPath.Combine(dir, "00007000");
                var original = IOPath.Combine(dir, "00005000");
                var type = Directory.Exists(god) ? "GOD" : Directory.Exists(original) ? "Original Xbox" : null;
                if (type is null) continue;
                lines.Add($"{name} • {type}\n{dir}");
            }

            var xexRoot = IOPath.Combine(OutputRoot, "XEX");
            if (Directory.Exists(xexRoot))
            {
                foreach (var dir in Directory.EnumerateDirectories(xexRoot))
                    lines.Add($"{IOPath.GetFileName(dir)} • XEX Folder\n{dir}");
            }

            _libraryText.Text = lines.Count == 0 ? "Library is empty." : string.Join("\n\n", lines);
        }
        catch (Exception ex) { _libraryText.Text = ex.Message; }
    }

    void RefreshQueueAdapter()
    {
        if (_queueList is null) return;
        var items = _queue.Select(x =>
        {
            var name = x.Info?.DisplayName ?? IOPath.GetFileNameWithoutExtension(x.LocalPath);
            var meta = x.Info is null ? "" : $" • {x.Info.TitleIdHex} • {x.Info.DiscKind}";
            return $"{name}{meta}\n{x.Status}";
        }).ToList();

        _queueAdapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleListItem1, items);
        _queueList.Adapter = _queueAdapter;
    }

    void AppendActivity(string text)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {text}";
        if (_activityText is not null)
        {
            var old = _activityText.Text ?? string.Empty;
            _activityText.Text = string.IsNullOrEmpty(old) ? line : old + "\n" + line;
        }
    }

    void LoadSettings()
    {
        var saved = GetPrefs().GetString("deploy_uri", null);
        if (!string.IsNullOrWhiteSpace(saved))
        {
            try { _deployTreeUri = Android.Net.Uri.Parse(saved); } catch { }
        }
    }

    Android.Content.ISharedPreferences GetPrefs() =>
        GetSharedPreferences("xenonforge", FileCreationMode.Private)!;

    LinearLayout PageColumn()
    {
        var p = new LinearLayout(this) { Orientation = Orientation.Vertical };
        p.SetPadding(Dp(16), Dp(10), Dp(16), Dp(20));
        return p;
    }

    TextView Heading(string text)
    {
        var v = new TextView(this) { Text = text, TextSize = 22f };
        v.SetTypeface(null, global::Android.Graphics.TypefaceStyle.Bold);
        v.SetTextColor(Color.White);
        return v;
    }

    TextView SectionTitle(string text)
    {
        var v = new TextView(this) { Text = text, TextSize = 12f };
        v.SetTypeface(null, global::Android.Graphics.TypefaceStyle.Bold);
        v.SetTextColor(Color.Rgb(64, 224, 208));
        return v;
    }

    TextView Muted(string text)
    {
        var v = new TextView(this) { Text = text, TextSize = 12f };
        v.SetTextColor(Color.Rgb(145, 162, 187));
        return v;
    }

    Button MakeButton(string text)
    {
        var b = new Button(this) { Text = text, TextSize = 12f };
        b.SetTextColor(Color.White);
        return b;
    }

    static T ApplyLayout<T>(T view, ViewGroup.LayoutParams p) where T : View
    {
        view.LayoutParameters = p;
        return view;
    }

    LinearLayout.LayoutParams Margin(int left = 0, int top = 0, int right = 0, int bottom = 0)
    {
        var p = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        p.SetMargins(Dp(left), Dp(top), Dp(right), Dp(bottom));
        return p;
    }

    LinearLayout.LayoutParams Match() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);

    int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);

    void SetBusy(bool value) => _busy = value;

    void SetStatus(string text)
    {
        if (_status is null) return;
        RunOnUiThread(() => _status.Text = text);
    }

    void ShowError(string message) => RunOnUiThread(() =>
        new AlertDialog.Builder(this)
            .SetTitle("XenonForge")
            .SetMessage(message)
            .SetPositiveButton("OK", (_, _) => { })
            .Show());

    void ShowInfo(string message) => RunOnUiThread(() =>
        new AlertDialog.Builder(this)
            .SetTitle("XenonForge")
            .SetMessage(message)
            .SetPositiveButton("OK", (_, _) => { })
            .Show());

    static string FormatBytes(long value)
    {
        if (value >= 1024L * 1024 * 1024) return $"{value / 1024d / 1024d / 1024d:0.00} GB";
        if (value >= 1024L * 1024) return $"{value / 1024d / 1024d:0.0} MB";
        return $"{value / 1024d:0} KB";
    }

    static string SanitizeFileName(string value)
    {
        foreach (var ch in IOPath.GetInvalidFileNameChars()) value = value.Replace(ch, '_');
        return value;
    }

    static string GetUniquePath(string directory, string fileName)
    {
        var candidate = IOPath.Combine(directory, fileName);
        if (!File.Exists(candidate)) return candidate;
        var stem = IOPath.GetFileNameWithoutExtension(fileName);
        var ext = IOPath.GetExtension(fileName);
        for (var i = 2; i < 10000; i++)
        {
            candidate = IOPath.Combine(directory, $"{stem} ({i}){ext}");
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

    sealed class GameRow
    {
        public string LocalPath { get; set; } = string.Empty;
        public XboxTitleInfo? Info { get; set; }
        public GodConversionResult? Result { get; set; }
        public string? XexOutputFolder { get; set; }
        public string Status { get; set; } = "Queued";
        public string? Error { get; set; }
    }

    sealed record TreeFile(Android.Net.Uri Uri, string Name);
}
