using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TerrainTool.Core.Campaign;
using TerrainTool.Core.Models;

namespace ModelEditor;

/// <summary>
/// Finestra principale: elenco dei modelli (aerei, elicotteri, mezzi, navi, armi, tutti per numero) e a destra
/// la vista 3D con DOF / switch / slot, l'editor di Parent.dat e le informazioni sul modello.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly TextBox _data = new() { Width = 280, Margin = new Thickness(0, 0, 4, 0) };
    private readonly ComboBox _theater = new() { MinWidth = 240, Margin = new Thickness(0, 0, 10, 0), ToolTip = "Theater of BMS (theater.lst): its database names the models and is the one shown in the Database tab" };
    private BmsTheater? _th;
    private readonly TextBox _search = new() { Width = 180, Margin = new Thickness(0, 0, 10, 0) };
    private readonly ComboBox _cat = new() { Width = 170, Margin = new Thickness(0, 0, 10, 0) };
    private readonly ListBox _list = new() { Width = 330, Margin = new Thickness(6, 0, 0, 6) };
    private readonly TextBlock _status = new() { Margin = new Thickness(8, 2, 8, 6), TextWrapping = TextWrapping.Wrap };
    private readonly TabControl _tabs = new() { Margin = new Thickness(4, 0, 6, 4) };
    private readonly ModelViewer _viewer = new();
    private readonly ParentDatPanel _parent = new();
    private readonly TextBox _info = new() { IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 12, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TabItem _infoTab;
    private readonly TabControl _main = new() { Margin = new Thickness(4, 0, 4, 0) };
    private readonly TabItem _modelsTab;
    private readonly DatabasePanel _dbPanel;
    private List<ModelEntry> _all = new();
    private List<ModelEntry> _weapons = new();
    private CampaignDb? _db;
    private Dictionary<int, string> _modelNames = new();
    private ModelEntry? _current;
    private readonly int? _startModel;

    /// <summary>Cartella del programma (quella con build.bat), per impostazioni, copie e foto.</summary>
    public static string Root()
    {
        if (global::TerrainTool.Core.SuiteLayout.ProgramFolder() is { } pf) return pf;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BMS Model Editor");
    }

    private static string DataSetting => Path.Combine(Root(), "Settings", "bms_data_folder.txt");
    private string ObjectsDir => Path.Combine(_data.Text.Trim(), "Terrdata", "objects");
    private string ModelsRoot => Path.Combine(ObjectsDir, "Models");

    /// <summary>Theater chosen in the BMS Suite main menu: selected the first time the theater list is read.</summary>
    private string? _startTheater;

    public MainWindow(string? data, int? startModel, string? theater = null)
    {
        _startModel = startModel;
        Title = "BMS Model Editor";
        Width = 1600; Height = 950; MinWidth = 1100; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        try { if (data == null && File.Exists(DataSetting)) data = File.ReadAllText(DataSetting).Trim(); } catch { }
        _data.Text = data ?? FindBmsData() ?? @"C:\Falcon BMS 4.38\Data";
        _startTheater = theater;

        var top = new WrapPanel { Margin = new Thickness(6) };
        void L(string s) => top.Children.Add(new TextBlock { Text = s, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        L("BMS Data folder"); top.Children.Add(_data);
        top.Children.Add(Btn("…", BrowseData, "Choose the BMS Data folder (for example C:\\Falcon BMS 4.38\\Data)"));
        L("  Theater"); top.Children.Add(_theater);
        top.Children.Add(Btn("Reload", () => LoadTheaters(), "Reads the theater list and the database again"));
        L("   Search"); top.Children.Add(_search);
        L("Show"); top.Children.Add(_cat);
        top.Children.Add(Btn("Open folder…", OpenExternal, "Open a model folder outside BMS (for example a Blender export with Parent.dat, Model_N.bml, Materials.mtl). You can also drag the folder onto the window."));
        top.Children.Add(Btn("Export to Blender…", () => new BlenderExportWindow(_current, n => _modelNames.GetValueOrDefault(n, "")) { Owner = this }.Show(),
            "Converts the model shown (or several model numbers) to a Blender .blend for the official BMS Blender plugin: LODs, DOFs, switches, slots, materials and textures"));
        top.Children.Add(Btn("Check all models…", CheckAll, "Checks every model folder (Parent.dat, LOD files, DOF / switch numbers, textures)"));
        top.Children.Add(Btn("Backups…", () =>
        {
            var w = new BackupWindow { Owner = this };
            w.Restored += f => { if (_current?.ModelDir != null && f.StartsWith(_current.ModelDir, StringComparison.OrdinalIgnoreCase)) Show(_current); };
            w.ShowDialog();
        }, "Files changed by the Model Editor, with their previous versions"));
        foreach (var c in new[] { "All", "Aircraft", "Helicopters", "Ground vehicles", "Ships", "Weapons", "All models by number" }) _cat.Items.Add(c);
        _cat.SelectedIndex = 0;
        _cat.SelectionChanged += (_, _) => FillList();
        _search.TextChanged += (_, _) => FillList();
        _list.SelectionChanged += (_, _) => { if (_list.SelectedItem is ListBoxItem { Tag: ModelEntry m }) Show(m); };

        _viewer.Status += s => _status.Text = s;
        _parent.Status += s => _status.Text = s;
        _parent.Saved += () => { _viewer.Reload(); FillInfo(); };
        _tabs.Items.Add(new TabItem { Header = "3D view", Content = _viewer });
        _tabs.Items.Add(new TabItem { Header = "Parent.dat", Content = _parent });
        _infoTab = new TabItem { Header = "Info", Content = _info };
        _tabs.Items.Add(_infoTab);
        _tabs.SelectionChanged += (_, e) => { if (e.Source == _tabs && _tabs.SelectedItem == _infoTab) FillInfo(); };

        var note = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(8, 0, 8, 4), FontSize = 11,
            Text = "Models of Falcon BMS 4.38 (Terrdata\\objects\\Models). The 3D view and the checks only read; saving Parent.dat writes into BMS (BMS closed, backup copy first). " +
                   "DOF and switch names come from the official BMS Blender exporter (Data\\DOF.xml, Data\\switch.xml).",
        };
        var body = new DockPanel();
        DockPanel.SetDock(_list, Dock.Left); body.Children.Add(_list);
        body.Children.Add(_tabs);
        // due parti: i modelli 3D e il database del teatro (sola consultazione), collegate da "View 3D"
        _dbPanel = new DatabasePanel(() => _data.Text.Trim(), dir =>
        {
            _main.SelectedItem = _modelsTab;
            if (!ModelPlaces.IsExternal(dir) && int.TryParse(Path.GetFileName(dir), out int id)) OpenModel(id); else OpenExternalPath(dir);
        });
        _modelsTab = new TabItem { Header = "Models", Content = body };
        var dbTab = new TabItem { Header = "Database", Content = _dbPanel };
        _main.Items.Add(_modelsTab);
        _main.Items.Add(dbTab);
        _main.SelectionChanged += (_, e) => { if (e.Source == _main && _main.SelectedItem == dbTab) _dbPanel.EnsureLoaded(); };
        _dbPanel.SetTheater(null, false);
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        DockPanel.SetDock(note, Dock.Top); root.Children.Add(note);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        root.Children.Add(_main);
        Content = root;

        _viewer.Names = BmsNames.Load(Path.Combine(Root(), "Data"));
        BmlScene.AddLightSwitches(_viewer.Names);
        if (_viewer.Names.Dofs.Count == 0) _status.Text = "Data\\DOF.xml not found: DOFs and switches are shown by number only.";
        _viewer.Clear("Choose a model on the left.");
        AllowDrop = true;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } f) OpenExternalPath(f[0]); };
        Loaded += (_, _) => LoadTheaters();
        _theater.SelectionChanged += async (_, _) =>
        {
            _th = (_theater.SelectedItem as ComboBoxItem)?.Tag as BmsTheater;
            try { if (_th != null) File.WriteAllText(TheaterSetting, _th.Name); } catch { }
            _dbPanel.SetTheater(_th, _main.SelectedItem != _modelsTab);
            await LoadAsync();
        };
        Closing += (_, e) =>
        {
            if (_parent.Dirty && MessageBox.Show(this, "Parent.dat has changes that are not saved. Close anyway?", Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                e.Cancel = true;
        };
    }

    private static Button Btn(string t, Action a, string? tip = null)
    {
        var b = new Button { Content = t, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0), ToolTip = tip };
        b.Click += (_, _) => a();
        return b;
    }

    /// <summary>Cartella Data di BMS dal registro (installazione standard).</summary>
    private static string? FindBmsData()
    {
        try
        {
            foreach (var key in new[] { @"SOFTWARE\WOW6432Node\Benchmark Sims\Falcon BMS 4.38", @"SOFTWARE\Benchmark Sims\Falcon BMS 4.38" })
            {
                using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
                if (k?.GetValue("baseDir") is string b && Directory.Exists(Path.Combine(b, "Data"))) return Path.Combine(b, "Data");
            }
        }
        catch { }
        return null;
    }

    private void BrowseData()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Falcon BMS Data folder (the one with Terrdata, Campaign, Art…)" };
        if (dlg.ShowDialog(this) == true) { _data.Text = dlg.FolderName; LoadTheaters(); }
    }

    private static string TheaterSetting => Path.Combine(Root(), "Settings", "theater.txt");

    /// <summary>Theaters of theater.lst of the BMS Data folder; the last one used is chosen again.</summary>
    private void LoadTheaters()
    {
        string data = _data.Text.Trim();
        _theater.Items.Clear();
        if (!Directory.Exists(Path.Combine(data, "Terrdata"))) { _status.Text = $"{data}: not a BMS Data folder (no Terrdata). Choose it with …"; return; }
        try { Directory.CreateDirectory(Path.GetDirectoryName(DataSetting)!); File.WriteAllText(DataSetting, data); } catch { }
        string kto = Path.Combine(data, "Terrdata", "objects");
        foreach (var t in BmsTheater.List(data).Where(t => Directory.Exists(t.ObjectDir)))
        {
            bool shared = TerrainTool.Core.Database.DbInstaller.IsShared(data, t.ObjectDir);
            _theater.Items.Add(new ComboBoxItem { Content = t.Name + (shared ? "  (shared database)" : ""), Tag = t, ToolTip = t.ObjectDir });
        }
        if (_theater.Items.Count == 0) { _status.Text = "No theater in theater.lst: only the BMS models are shown."; _th = null; _ = LoadAsync(); return; }
        string? last = null;
        try { if (File.Exists(TheaterSetting)) last = File.ReadAllText(TheaterSetting).Trim(); } catch { }
        if (_startTheater != null) { last = _startTheater; _startTheater = null; }
        int i = _theater.Items.OfType<ComboBoxItem>().ToList().FindIndex(x => x.Tag is BmsTheater t && string.Equals(t.Name, last, StringComparison.OrdinalIgnoreCase));
        _theater.SelectedIndex = Math.Max(0, i);
    }

    private async Task LoadAsync()
    {
        if (!Directory.Exists(ModelsRoot)) { _status.Text = $"{ModelsRoot} not found: choose the BMS Data folder"; return; }
        try { Directory.CreateDirectory(Path.GetDirectoryName(DataSetting)!); File.WriteAllText(DataSetting, _data.Text.Trim()); } catch { }
        ModelPlaces.BmsModelsRoot = ModelsRoot;
        string? thModels = _th != null ? Path.Combine(_th.ObjectDir, "Models") : null;
        ModelPlaces.TheaterModelsRoot = thModels != null && Directory.Exists(thModels) && ModelPlaces.IsExternal(Path.Combine(thModels, "0")) ? thModels : null;
        _status.Text = "Reading the BMS database…";
        try
        {
            // il database del teatro (nomi) e per i modelli prima la sua cartella Models, poi quella di BMS
            var dirs = new[] { _th?.ObjectDir ?? ObjectsDir, ObjectsDir }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var (db, veh, wpn) = await Task.Run(() => { var db = CampaignDb.Load(dirs); return (db, ModelCatalog.Vehicles(db), ModelCatalog.Weapons(db)); });
            _db = db; _all = veh.Concat(wpn).ToList(); _weapons = wpn;
            _viewer.Weapons = wpn;
            _modelNames = _all.GroupBy(m => m.ModelId).ToDictionary(g => g.Key, g => string.Join(" / ", g.Select(m => m.Name).Distinct().Take(3)));
            _status.Text = $"{_th?.Name ?? "BMS"}: {veh.Count} vehicles and {wpn.Count} weapons with a 3D model ({db.CtPath})" +
                           (ModelPlaces.TheaterModelsRoot != null ? $"; the theater's own models first ({ModelPlaces.TheaterModelsRoot}), then BMS's" : "");
            FillList();
            if (_startModel is int sm) OpenModel(sm);
        }
        catch (Exception ex) { _status.Text = "ERROR: " + ex.Message; }
    }

    private void FillList()
    {
        _list.Items.Clear();
        string cat = _cat.SelectedItem as string ?? "All", q = _search.Text.Trim();
        IEnumerable<ModelEntry> src;
        if (cat == "All models by number")
            src = new[] { ModelPlaces.TheaterModelsRoot, ModelsRoot }.Where(d => d != null && Directory.Exists(d))
                    .SelectMany(d => Directory.EnumerateDirectories(d!)).Select(Path.GetFileName).Where(n => int.TryParse(n, out _))
                    .Select(n => int.Parse(n!, CultureInfo.InvariantCulture)).Distinct().OrderBy(n => n)
                    .Select(n => new ModelEntry(-1, $"#{n}  {_modelNames.GetValueOrDefault(n, "")}", "Model", n, ModelPlaces.DirFor(n)));
        else src = _all.Where(m => cat == "All" || m.Category == cat);
        foreach (var m in src.Where(m => q.Length == 0 || m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || m.ModelId.ToString(CultureInfo.InvariantCulture) == q))
            _list.Items.Add(new ListBoxItem { Content = m.Category == "Model" ? m.Name : $"{m.Name}   ({m.Category}, model {m.ModelId}{(m.ModelDir == null ? ", missing" : "")})", Tag = m });
    }

    /// <summary>Apre il modello con quel numero (anche se nessuna voce del database lo usa).</summary>
    public void OpenModel(int id)
    {
        var hit = _list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is ModelEntry m && m.ModelId == id);
        if (hit != null) { _list.SelectedItem = hit; _list.ScrollIntoView(hit); return; }
        Show(new ModelEntry(-1, $"#{id}  {_modelNames.GetValueOrDefault(id, "")}".Trim(), "Model", id, ModelPlaces.DirFor(id)));
    }

    private void Show(ModelEntry m)
    {
        if (_parent.Dirty && !ReferenceEquals(m, _current) &&
            MessageBox.Show(this, "Parent.dat has changes that are not saved. Leave them?", Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _current = m;
        Title = m.Category == "External" ? $"BMS Model Editor — {m.ModelDir}" : $"BMS Model Editor — {m.Name} (model {m.ModelId})";
        if (m.ModelDir == null || !Directory.Exists(m.ModelDir))
        {
            _viewer.Clear($"{m.Name}: model folder not found (model {m.ModelId}).");
            _info.Text = "";
            return;
        }
        _viewer.Open(m.ModelDir, m.Name);
        _parent.Open(m.ModelDir);
        if (_tabs.SelectedItem == _infoTab) FillInfo();
    }

    private static string ExternalSetting => Path.Combine(Root(), "Settings", "last_external_folder.txt");

    private void OpenExternal()
    {
        string? last = null;
        try { if (File.Exists(ExternalSetting)) last = File.ReadAllText(ExternalSetting).Trim(); } catch { }
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Model folder (with Parent.dat), for example a Blender export" };
        if (last != null && Directory.Exists(last)) dlg.InitialDirectory = Path.GetDirectoryName(last) ?? last;
        if (dlg.ShowDialog(this) == true) OpenExternalPath(dlg.FolderName);
    }

    /// <summary>Apre una cartella di modello qualsiasi (non viene copiato niente in BMS).</summary>
    private void OpenExternalPath(string path)
    {
        var dir = ModelPlaces.FindModelFolder(path);
        if (dir == null) { MessageBox.Show(this, $"No Parent.dat in\n{path}\n\nChoose the folder that contains Parent.dat and the Model_N.bml files.", "Open folder", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (!ModelPlaces.IsExternal(dir) && int.TryParse(Path.GetFileName(dir), out int id)) { OpenModel(id); return; }
        try { Directory.CreateDirectory(Path.GetDirectoryName(ExternalSetting)!); File.WriteAllText(ExternalSetting, dir); } catch { }
        _list.SelectedItem = null;
        Show(new ModelEntry(-1, "External: " + Path.GetFileName(dir), "External", -1, dir));
        _status.Text = $"Opened {dir} (outside BMS: nothing is copied into BMS). Shared textures and numbered textures are looked for in BMS too.";
    }

    private void CheckAll()
    {
        if (!Directory.Exists(ModelsRoot)) { _status.Text = $"{ModelsRoot} not found"; return; }
        new CheckAllWindow(ModelsRoot, n => _modelNames.GetValueOrDefault(n, ""), id => { Activate(); OpenModel(id); }) { Owner = this, Names = _viewer.Names }.Show();
    }

    // ───────────────────────── Info

    private void FillInfo()
    {
        var m = _current;
        if (m?.ModelDir == null || !Directory.Exists(m.ModelDir)) { _info.Text = ""; return; }
        var sb = new StringBuilder();
        bool external = m.Category == "External";
        sb.AppendLine(external ? $"External folder (not in BMS)   {m.ModelDir}" : $"Model {m.ModelId}   {m.ModelDir}");
        sb.AppendLine();
        if (_db != null && !external)
        {
            var users = _db.Ct.Where(kv => kv.Value.Graphics == m.ModelId).OrderBy(kv => kv.Key).ToList();
            sb.AppendLine($"Used by {users.Count} CT entr{(users.Count == 1 ? "y" : "ies")}:");
            foreach (var (ct, c) in users.Take(60))
            {
                string kind = c.EntityType switch { 1 => "feature", 3 => "objective", 4 => "unit", 5 => "vehicle", 6 => "weapon", _ => $"type {c.EntityType}" };
                string name = c.EntityType == 5 ? _db.VehicleName(ct) : c.EntityType == 6 ? _db.Wcd.GetValueOrDefault(c.EntityIdx, "") : "";
                sb.AppendLine($"  CT {ct,5}  {kind,-9} {name}   (domain {c.Domain}, class {c.Class}, type {c.Type}, subtype {c.SubType}, specific {c.Specific})");
            }
            if (users.Count > 60) sb.AppendLine($"  … {users.Count - 60} more");
            sb.AppendLine();
        }
        sb.AppendLine("Files:");
        foreach (var f in Directory.EnumerateFiles(m.ModelDir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var fi = new FileInfo(f);
            sb.AppendLine($"  {fi.Name,-34} {fi.Length / 1024.0,10:N1} KB   {fi.LastWriteTime:yyyy-MM-dd HH:mm}");
        }
        sb.AppendLine();
        try
        {
            var p = BmlModel.ReadParent(m.ModelDir);
            if (p != null)
            {
                var tex = ModelPlaces.TexDirsFor(m.ModelDir);
                for (int i = 0; i < p.Lods.Count; i++)
                {
                    sb.Append($"LOD {i}  {p.Lods[i].File}  up to {p.Lods[i].Distance:0} ft: ");
                    try
                    {
                        var sc = BmlScene.Load(p, i, 0, ModelPlaces.RootFor(m.ModelDir), tex);
                        var pose = sc.Pose();
                        sb.AppendLine($"BML v{sc.Version}, {sc.PrimitiveCount} primitives, {pose.Triangles:N0} triangles at rest, {sc.Dofs.Count} DOFs, {sc.Switches.Count} switches, {sc.Slots.Count} slots, {sc.MaterialCount} materials");
                        if (i == 0)
                        {
                            sb.AppendLine("  Materials (first livery):");
                            foreach (var (name, t) in sc.MaterialList) sb.AppendLine($"    {name,-34} {(t == null ? "(no texture found)" : t)}");
                            if (sc.Dofs.Count > 0) sb.AppendLine("  DOFs: " + string.Join(", ", sc.Dofs.Select(d => $"{d.Number} {_viewer.Names.Dof(d.Number)}")));
                            if (sc.Switches.Count > 0) sb.AppendLine("  Switches: " + string.Join(", ", sc.Switches.Select(s => $"{s.Number}/{s.Branch} {_viewer.Names.Switch(s.Number, s.Branch)}")));
                        }
                    }
                    catch (Exception e) { sb.AppendLine("cannot be read – " + e.Message); }
                }
            }
        }
        catch (Exception e) { sb.AppendLine("Parent.dat: " + e.Message); }
        _info.Text = sb.ToString();
    }
}
