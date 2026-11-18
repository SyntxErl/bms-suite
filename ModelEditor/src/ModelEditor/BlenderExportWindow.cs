using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TerrainTool.Core.Models;

namespace ModelEditor;

/// <summary>
/// Export to Blender: one or more models (buildings, aircraft, vehicles, weapons...) converted to .blend files laid out for
/// the official BMS Blender plugin. Blender runs in the background; nothing of BMS is changed.
/// </summary>
public sealed class BlenderExportWindow : Window
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
    private readonly TextBox _models = new() { MinWidth = 380 };
    private readonly string? _externalDir;
    private readonly Func<int, string> _nameOf;
    private readonly RadioButton _allLods = new() { Content = "All LODs", IsChecked = true, Margin = new Thickness(0, 0, 12, 0) };
    private readonly RadioButton _lod0 = new() { Content = "Only the most detailed (LOD 0)" };
    private readonly ComboBox _texSize = new() { Width = 150 };
    private readonly TextBox _livery = new() { Width = 50, Text = "1" };
    private readonly TextBox _out = new() { MinWidth = 420 };
    private readonly ComboBox _blender = new() { MinWidth = 420 };
    private readonly CheckBox _open = new() { Content = "Open in Blender when done (one model)", IsChecked = true };
    private readonly TextBox _log = new()
    {
        IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    private readonly Button _go = new() { Content = "Convert", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
    private bool _running;

    private static string Setting(string n) => Path.Combine(MainWindow.Root(), "Settings", n);
    private static string? Read(string n) { try { return File.Exists(Setting(n)) ? File.ReadAllText(Setting(n)).Trim() : null; } catch { return null; } }
    private static void Save(string n, string v) { try { Directory.CreateDirectory(Path.GetDirectoryName(Setting(n))!); File.WriteAllText(Setting(n), v); } catch { } }

    /// <param name="current">model shown in the Model Editor (number, or an external folder)</param>
    public BlenderExportWindow(ModelEntry? current, Func<int, string> nameOf)
    {
        _nameOf = nameOf;
        Title = "Export to Blender";
        Width = 980; Height = 720; MinWidth = 760; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (current is { Category: "External", ModelDir: { } ed }) { _externalDir = ed; _models.Text = ed; _models.IsEnabled = false; }
        else if (current is { ModelId: >= 0 }) _models.Text = current.ModelId.ToString(Ci);
        foreach (var s in new[] { "4096 (as in BMS)", "2048", "1024" }) _texSize.Items.Add(s);
        _texSize.SelectedIndex = 0;
        _out.Text = Read("blender_out.txt") ?? Path.Combine(MainWindow.Root(), "Blender");

        var g = new Grid { Margin = new Thickness(10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int row = 0;
        void Row(string label, UIElement e, string? tip = null)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, Margin = new Thickness(0, 4, 10, 4), VerticalAlignment = VerticalAlignment.Center, ToolTip = tip };
            Grid.SetRow(l, row); g.Children.Add(l);
            if (e is FrameworkElement fe) { fe.Margin = new Thickness(0, 3, 0, 3); fe.ToolTip ??= tip; }
            Grid.SetRow(e, row); Grid.SetColumn(e, 1); g.Children.Add(e);
            row++;
        }
        Row(_externalDir != null ? "Model folder" : "Models", _models,
            "Model numbers (Terrdata\\objects\\Models), separated by commas or spaces, ranges too: 1052, 917, 1700-1705");
        var lods = new StackPanel { Orientation = Orientation.Horizontal };
        lods.Children.Add(_allLods); lods.Children.Add(_lod0);
        Row("LODs", lods);
        var tex = new StackPanel { Orientation = Orientation.Horizontal };
        tex.Children.Add(_texSize);
        tex.Children.Add(new TextBlock { Text = "   Livery (texture set)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        tex.Children.Add(_livery);
        Row("Textures", tex, "Largest texture size: smaller is faster and lighter. The textures are written as PNG next to the .blend.");
        var outRow = new DockPanel();
        var browse = new Button { Content = "…", Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) => { var d = new Microsoft.Win32.OpenFolderDialog { Title = "Folder for the Blender files" }; if (d.ShowDialog(this) == true) _out.Text = d.FolderName; };
        DockPanel.SetDock(browse, Dock.Right); outRow.Children.Add(browse); outRow.Children.Add(_out);
        Row("Save in", outRow, "Each model gets its own folder: <number> <name>\\<name>.blend and its textures");
        var bl = new DockPanel();
        var bbrowse = new Button { Content = "…", Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(6, 0, 0, 0) };
        bbrowse.Click += (_, _) =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "blender.exe", Filter = "Blender|blender.exe|Programs|*.exe" };
            if (d.ShowDialog(this) == true) { _blender.Items.Insert(0, new ComboBoxItem { Content = d.FileName, Tag = d.FileName }); _blender.SelectedIndex = 0; }
        };
        DockPanel.SetDock(bbrowse, Dock.Right); bl.Children.Add(bbrowse); bl.Children.Add(_blender);
        Row("Blender", bl, "Blender 3.6 LTS with the official BMS Blender plugin (bms-blender-plugin) installed and enabled");
        Row("", _open);

        var help = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, FontSize = 11.5, Margin = new Thickness(10, 0, 10, 6),
            Text = "Makes a .blend for the official BMS Blender plugin: one collection per LOD (in the plugin's LOD list), DOFs, switches and slots " +
                   "as the plugin's objects with their numbers and values, meshes with normals and UVs, materials with their textures. " +
                   "Edit the model in Blender and export it again with File → Export → F4-BMS (.bml). Materials.mtl and Parent.dat of the model are copied next to the .blend. " +
                   "Nothing in BMS is changed.",
        };
        var bar = new WrapPanel { Margin = new Thickness(10, 0, 10, 6) };
        bar.Children.Add(_go);
        var openFolder = new Button { Content = "Open the folder", Padding = new Thickness(10, 3, 10, 3) };
        openFolder.Click += (_, _) => { try { Directory.CreateDirectory(_out.Text.Trim()); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_out.Text.Trim()}\"") { UseShellExecute = true }); } catch { } };
        bar.Children.Add(openFolder);
        _go.Click += async (_, _) => await ConvertAsync();

        var root = new DockPanel();
        DockPanel.SetDock(g, Dock.Top); root.Children.Add(g);
        DockPanel.SetDock(help, Dock.Top); root.Children.Add(help);
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        _log.Margin = new Thickness(10, 0, 10, 10);
        root.Children.Add(_log);
        Content = root;
        Loaded += (_, _) => FillBlender();
        Closing += (_, e) => { if (_running) e.Cancel = MessageBox.Show(this, "A conversion is running. Close anyway (Blender keeps running until it ends)?", Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes; };
    }

    private void FillBlender()
    {
        _blender.Items.Clear();
        string? saved = Read("blender_exe.txt");
        if (saved != null && File.Exists(saved)) _blender.Items.Add(new ComboBoxItem { Content = saved, Tag = saved });
        List<(string Exe, string Version, bool Plugin)> found;
        try { found = BlenderExport.FindBlender(); } catch { found = new(); }
        foreach (var f in found.Where(f => !string.Equals(f.Exe, saved, StringComparison.OrdinalIgnoreCase)))
            _blender.Items.Add(new ComboBoxItem { Content = $"{f.Exe}   (Blender {f.Version}{(f.Plugin ? ", BMS plugin installed" : ", BMS plugin not found")})", Tag = f.Exe });
        if (_blender.Items.Count == 0)
        {
            _blender.Items.Add(new ComboBoxItem { Content = "Blender not found: choose blender.exe with … (or only the data folder is made)", Tag = null });
            Log("Blender was not found. Install Blender 3.6 LTS (blender.org) and the official BMS Blender plugin, or choose blender.exe with \"…\".");
        }
        _blender.SelectedIndex = 0;
    }

    private void Log(string s) { _log.AppendText(s + "\n"); _log.ScrollToEnd(); }

    /// <summary>"1052, 917 1700-1702" → numbers.</summary>
    private static List<int> Numbers(string s)
    {
        var res = new List<int>();
        foreach (var t in s.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var r = t.Split('-');
            if (r.Length == 2 && int.TryParse(r[0], NumberStyles.Integer, Ci, out int a) && int.TryParse(r[1], NumberStyles.Integer, Ci, out int b) && b >= a && b - a < 2000)
                for (int i = a; i <= b; i++) res.Add(i);
            else if (int.TryParse(t, NumberStyles.Integer, Ci, out int n)) res.Add(n);
        }
        return res.Distinct().ToList();
    }

    private static string Safe(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var r = new string(s.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return r.Length > 60 ? r[..60] : r;
    }

    private async Task ConvertAsync()
    {
        if (_running) return;
        var jobs = new List<(string Dir, string Label)>();
        if (_externalDir != null) jobs.Add((_externalDir, Path.GetFileName(_externalDir.TrimEnd('\\', '/'))));
        else
            foreach (int n in Numbers(_models.Text))
            {
                string dir = ModelPlaces.DirFor(n), name = _nameOf(n);
                if (!File.Exists(Path.Combine(dir, "Parent.dat"))) { Log($"model {n}: no Parent.dat in {dir}"); continue; }
                jobs.Add((dir, name.Length > 0 ? $"{n} {name}" : n.ToString(Ci)));
            }
        if (jobs.Count == 0) { Log("Nothing to convert: write the model numbers (for example 1052, 917)."); return; }
        string outRoot = _out.Text.Trim();
        try { Directory.CreateDirectory(outRoot); } catch (Exception ex) { Log($"{outRoot}: {ex.Message}"); return; }
        Save("blender_out.txt", outRoot);
        string? blender = (_blender.SelectedItem as ComboBoxItem)?.Tag as string;
        if (blender != null) Save("blender_exe.txt", blender);
        int maxTex = _texSize.SelectedIndex switch { 1 => 2048, 2 => 1024, _ => 4096 };
        int livery = int.TryParse(_livery.Text.Trim(), NumberStyles.Integer, Ci, out int lv) ? Math.Max(0, lv - 1) : 0;
        bool onlyLod0 = _lod0.IsChecked == true;
        bool open = _open.IsChecked == true && jobs.Count == 1;

        _running = true; _go.IsEnabled = false;
        int ok = 0;
        try
        {
            foreach (var (dir, label) in jobs)
            {
                string folder = Path.Combine(outRoot, Safe(label));
                string blend = Path.Combine(folder, Safe(label) + ".blend");
                Log($"── {label}  ({dir})");
                var parent = BmlModel.ReadParent(dir);
                if (parent == null || parent.Lods.Count == 0) { Log("   Parent.dat has no LOD"); continue; }
                string root = ModelPlaces.RootFor(dir);
                var texDirs = ModelPlaces.TexDirsFor(dir);
                BlenderExport.Result r;
                try
                {
                    r = await Task.Run(() => BlenderExport.Prepare(parent, root, texDirs, folder, label, onlyLod0 ? new[] { 0 } : null,
                        Math.Min(livery, parent.TextureSets - 1), maxTex, s => Dispatcher.BeginInvoke(() => Log("   " + s))));
                }
                catch (Exception ex) { Log("   not converted: " + ex.Message); continue; }
                Log($"   {r.Lods} LOD(s), {r.Meshes} meshes, {r.Textures} textures");
                foreach (var n in r.Notes.Take(30)) Log("   note: " + n);
                if (blender == null)
                {
                    Log($"   data ready in {folder}: open Blender → Scripting → open bms_blender_import.py → set JSON_FILE to {r.Json} → Run Script.");
                    continue;
                }
                Log("   Blender is building the .blend…");
                var (done, log) = await Task.Run(() => BlenderExport.Run(blender, r.Json, blend));
                try { File.WriteAllText(Path.Combine(folder, "blender_log.txt"), log, new UTF8Encoding(false)); } catch { }
                foreach (var l in log.Split('\n').Where(l => l.StartsWith("[BMS import]", StringComparison.Ordinal) && !l.Contains(": done")).Take(40))
                    Log("   " + l.Trim());
                if (!done)
                {
                    Log("   .blend NOT made: see blender_log.txt in " + folder);
                    foreach (var l in log.Split('\n').Where(l => l.Contains("Error", StringComparison.OrdinalIgnoreCase)).Take(8)) Log("   " + l.Trim());
                    continue;
                }
                // the .blend is made: the intermediate data is not needed any more
                foreach (var f in new[] { "mesh.bin", "model.json" }) try { File.Delete(Path.Combine(folder, f)); } catch { }
                ok++;
                Log("   saved " + blend);
                if (open) try { Process.Start(new ProcessStartInfo(blender) { ArgumentList = { blend }, UseShellExecute = false }); } catch (Exception ex) { Log("   Blender not opened: " + ex.Message); }
            }
        }
        finally { _running = false; _go.IsEnabled = true; }
        Log($"Done: {ok} of {jobs.Count} .blend file(s) made in {outRoot}");
    }
}
