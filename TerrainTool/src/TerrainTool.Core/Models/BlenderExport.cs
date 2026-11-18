using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace TerrainTool.Core.Models;

/// <summary>
/// A BMS 3D model (Parent.dat + Model_N.bml, old numbered-texture models and PBR models) converted to a Blender .blend
/// laid out for the official BMS Blender plugin (bms-blender-plugin), so that it can be changed and exported again:
/// <list type="bullet">
/// <item>one collection per LOD, in the plugin's LOD list with its file suffix and viewing distance;</item>
/// <item>DOFs, switches and slots as the plugin's empties (number, type, limits, multiplier, flags, branch, on at start);</item>
/// <item>every primitive as a mesh with normals and UVs, its material with the textures (PNG) in nodes named as the
///   plugin expects (Albedo / ARMW / Normal / Emissive Texture); Materials.mtl and Parent.dat are copied next to the .blend.</item>
/// </list>
/// Prepare() writes the data folder (model.json, mesh.bin, textures, the import script); Run() starts Blender in the
/// background to build the .blend. Nothing of BMS is changed.
/// </summary>
public static class BlenderExport
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public sealed record Result(string DataDir, string Json, List<string> Notes, int Lods, int Meshes, int Textures);

    /// <summary>The import script run inside Blender.</summary>
    public static string Script()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("TerrainTool.Core.Models.bms_blender_import.py")
                      ?? throw new InvalidOperationException("bms_blender_import.py missing from TerrainTool.Core");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>
    /// Writes the data folder for Blender. lods = LOD indexes to convert (null = all); textureSet = the livery (0 = default);
    /// maxTexture = largest texture size kept (the PBR textures are 4096²).
    /// </summary>
    public static Result Prepare(BmlModel.ParentInfo parent, string modelsRoot, IReadOnlyList<string> textureDirs, string dataDir, string title,
                                 IList<int>? lods = null, int textureSet = 0, int maxTexture = 4096, Action<string>? progress = null)
    {
        Directory.CreateDirectory(dataDir);
        string texDir = Path.Combine(dataDir, "textures");
        Directory.CreateDirectory(texDir);
        var notes = new List<string>();
        var pngOf = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var materials = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);

        string? Png(string? dds, string kind)
        {
            if (dds == null) return null;
            if (pngOf.TryGetValue(dds, out var done)) return done;
            progress?.Invoke($"texture {Path.GetFileName(dds)}");
            var img = DdsImage.Load(dds, maxTexture);
            if (img == null) { notes.Add($"texture not read: {dds}"); return pngOf[dds] = null; }
            var px = img.Bgra;
            if (kind == "normal")
            {
                // BC5 has only X and Y: Z rebuilt (B of BGRA is index 0)
                bool noZ = true;
                for (int i = 0; i < px.Length && noZ; i += 4 * 97) if (px[i] != 0) noZ = false;
                if (noZ)
                    for (int i = 0; i < px.Length; i += 4)
                    {
                        double x = px[i + 2] / 127.5 - 1, y = px[i + 1] / 127.5 - 1;
                        px[i] = (byte)Math.Clamp(Math.Round((Math.Sqrt(Math.Max(0, 1 - x * x - y * y)) + 1) * 127.5), 0, 255);
                    }
            }
            bool alpha = false;
            if (img.HasAlpha && kind == "albedo")
                for (int i = 3; i < px.Length; i += 4) if (px[i] < 250) { alpha = true; break; }
            string baseName = Path.GetFileNameWithoutExtension(dds), name = baseName;
            for (int k = 2; !usedNames.Add(name); k++) name = $"{baseName}_{k}";
            string rel = "textures/" + name + ".png";
            PngWriter.WriteBgra(Path.Combine(dataDir, rel), img.Width, img.Height, px, alpha);
            return pngOf[dds] = rel;
        }

        void AddMaterial(BmlScene s, string name, string? oldTexture, bool glass, bool emissive)
        {
            if (materials.ContainsKey(name)) return;
            var m = new Dictionary<string, object?> { ["name"] = name, ["glass"] = glass, ["emissive"] = emissive };
            if (s.Pbr.TryGetValue(name, out var pbr))
            {
                m["template"] = pbr.Template;
                m["albedo"] = Png(pbr.Albedo, "albedo");
                m["armw"] = Png(pbr.Armw, "armw");
                m["normal"] = Png(pbr.Normal, "normal");
                m["emissiveMap"] = Png(pbr.Emissive, "emissive");
                m["emission"] = pbr.EmissionIntensity;
                m["twoSided"] = pbr.TwoSided;
                m["alpha"] = pbr.Alpha;
                m["glass"] = pbr.Glass;
                m["emissive"] = pbr.EmissiveOnly;
            }
            else
            {
                m["template"] = s.Version == 1 ? "old model (numbered texture)" : "";
                m["albedo"] = Png(oldTexture, "albedo");
            }
            materials[name] = m;
        }

        var lodList = new List<int>();
        for (int i = 0; i < parent.Lods.Count; i++) if (lods == null || lods.Contains(i)) lodList.Add(i);
        string binPath = Path.Combine(dataDir, "mesh.bin");
        int meshes = 0;
        var json = new MemoryStream();
        using (var bin = new BinaryWriter(File.Create(binPath)))
        using (var w = new Utf8JsonWriter(json, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("title", title);
            w.WriteString("source", parent.Folder);
            w.WriteNumber("textureSets", parent.TextureSets);
            w.WriteNumber("textureSet", textureSet);
            w.WriteNumber("radius", parent.Radius);
            w.WriteString("bin", "mesh.bin");
            w.WriteStartArray("lods");
            foreach (int li in lodList)
            {
                progress?.Invoke($"LOD {li}");
                BmlScene s;
                try { s = BmlScene.Load(parent, li, textureSet, modelsRoot, textureDirs); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException)
                { notes.Add($"LOD {li} ({parent.Lods[li].File}) not read: {ex.Message}"); continue; }
                foreach (var n in s.Notes) notes.Add($"LOD {li}: {n}");
                var mats = s.Materials;
                string file = parent.Lods[li].File;
                string stem = Path.GetFileNameWithoutExtension(file);
                int us = stem.LastIndexOf('_');
                w.WriteStartObject();
                w.WriteNumber("index", li);
                w.WriteString("file", file);
                w.WriteString("suffix", us >= 0 ? stem[us..] : "_" + li.ToString(Ci));
                w.WriteNumber("distance", parent.Lods[li].Distance);
                w.WriteNumber("version", s.Version);
                w.WritePropertyName("root");
                void Node(BmlScene.TreeNode t)
                {
                    w.WriteStartObject();
                    w.WriteString("t", t.Type);
                    if (t.Type != "mesh") { w.WriteStartArray("frame"); foreach (var v in t.Frame) w.WriteNumberValue(v); w.WriteEndArray(); }
                    switch (t.Type)
                    {
                        case "dof":
                            var d = t.Dof!;
                            w.WriteNumber("num", d.Number);
                            w.WriteString("kind", d.Kind.ToString().ToUpperInvariant());
                            w.WriteNumber("min", d.Min); w.WriteNumber("max", d.Max); w.WriteNumber("mul", d.Multiplier); w.WriteNumber("flags", d.Flags);
                            w.WriteStartArray("scale"); foreach (var v in d.Scale) w.WriteNumberValue(float.IsFinite(v) ? v : 1); w.WriteEndArray();
                            w.WriteStartArray("tvec"); foreach (var v in d.T) w.WriteNumberValue(float.IsFinite(v) ? v : 0); w.WriteEndArray();
                            break;
                        case "switch":
                            w.WriteNumber("num", t.Switch!.Number); w.WriteNumber("branch", t.Switch.Branch); w.WriteBoolean("on", t.Switch.StartsOn);
                            break;
                        case "slot":
                            w.WriteNumber("num", t.Slot!.Number);
                            w.WriteStartArray("r"); foreach (var v in t.Slot.R) w.WriteNumberValue(float.IsFinite(v) ? v : 0); w.WriteEndArray();
                            break;
                        case "mesh":
                        {
                            string mn;
                            if (t.Light && (t.Material < 0 || s.Version == 1 && t.Texture == null))
                            {
                                mn = $"BML-Light-{t.Color & 0xFFFFFF:X6}";
                                if (!materials.ContainsKey(mn))
                                    materials[mn] = new Dictionary<string, object?> { ["name"] = mn, ["template"] = "light", ["emissive"] = true,
                                        ["color"] = new[] { (t.Color & 0xFF) / 255.0, ((t.Color >> 8) & 0xFF) / 255.0, ((t.Color >> 16) & 0xFF) / 255.0 } };
                            }
                            else if (t.Material >= 0 && t.Material < mats.Count)
                            {
                                mn = mats[t.Material].Name.Length > 0 ? mats[t.Material].Name : "BML-Default";
                                AddMaterial(s, mn, mats[t.Material].Texture, mats[t.Material].Glass, mats[t.Material].Emissive);
                            }
                            else mn = "BML-Default";
                            w.WriteString("mat", mn);
                            w.WriteNumber("off", bin.BaseStream.Position);
                            w.WriteNumber("vc", t.P.Length / 3);
                            w.WriteNumber("ic", t.I.Length);
                            if (t.Light) w.WriteBoolean("light", true);
                            foreach (var v in t.P) bin.Write(v);
                            foreach (var v in t.N) bin.Write(v);
                            foreach (var v in t.Uv) bin.Write(v);
                            foreach (var v in t.I) bin.Write(v);
                            meshes++;
                            break;
                        }
                    }
                    if (t.Children.Count > 0)
                    {
                        w.WriteStartArray("children");
                        foreach (var c in t.Children) Node(c);
                        w.WriteEndArray();
                    }
                    w.WriteEndObject();
                }
                Node(s.Tree());
                w.WriteEndObject();
            }
            w.WriteEndArray();
            if (!materials.ContainsKey("BML-Default")) materials["BML-Default"] = new Dictionary<string, object?> { ["name"] = "BML-Default", ["template"] = "" };
            w.WritePropertyName("materials");
            JsonSerializer.Serialize(w, materials.Values.ToList());
            w.WriteEndObject();
        }
        string jsonPath = Path.Combine(dataDir, "model.json");
        File.WriteAllBytes(jsonPath, json.ToArray());
        File.WriteAllText(Path.Combine(dataDir, "bms_blender_import.py"), Script(), new UTF8Encoding(false));
        foreach (var f in new[] { "Parent.dat", "Materials.mtl" })
            if (File.Exists(Path.Combine(parent.Folder, f))) CopyFile(Path.Combine(parent.Folder, f), Path.Combine(dataDir, f));
        foreach (var f in Directory.EnumerateFiles(parent.Folder, "*.mti")) CopyFile(f, Path.Combine(dataDir, Path.GetFileName(f)));
        return new Result(dataDir, jsonPath, notes, lodList.Count, meshes, pngOf.Values.Count(v => v != null));
    }

    /// <summary>Copy that can be overwritten next time (BMS files are often read-only).</summary>
    private static void CopyFile(string src, string dst)
    {
        if (File.Exists(dst)) File.SetAttributes(dst, FileAttributes.Normal);
        File.Copy(src, dst, true);
        File.SetAttributes(dst, FileAttributes.Normal);
    }

    // ─────────────────── Blender

    /// <summary>Blender installations found (newest first; those with the BMS plugin installed first).</summary>
    public static List<(string Exe, string Version, bool Plugin)> FindBlender()
    {
        var res = new List<(string, string, bool)>();
        var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }
            .Where(r => r.Length > 0).Distinct().ToList();
        var exes = new List<string>();
        foreach (var r in roots)
        {
            string bf = Path.Combine(r, "Blender Foundation");
            if (Directory.Exists(bf)) foreach (var d in Directory.EnumerateDirectories(bf)) exes.Add(Path.Combine(d, "blender.exe"));
            exes.Add(Path.Combine(r, "Steam", "steamapps", "common", "Blender", "blender.exe"));
        }
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (var e in exes.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string dir = Path.GetDirectoryName(e)!;
            // the version folder next to blender.exe ("3.6", "4.2"...)
            string ver = Directory.EnumerateDirectories(dir).Select(Path.GetFileName).Where(n => n != null && System.Text.RegularExpressions.Regex.IsMatch(n, @"^\d+\.\d+$"))
                .OrderByDescending(n => Version.Parse(n!)).FirstOrDefault() ?? "";
            bool plugin = ver.Length > 0 && (Directory.Exists(Path.Combine(appData, "Blender Foundation", "Blender", ver, "scripts", "addons", "bms_blender_plugin")) ||
                                             Directory.Exists(Path.Combine(dir, ver, "scripts", "addons", "bms_blender_plugin")));
            res.Add((e, ver, plugin));
        }
        return res.OrderByDescending(x => x.Item3).ThenByDescending(x => Version.TryParse(x.Item2, out var v) ? v : new Version(0, 0)).ToList();
    }

    /// <summary>Starts Blender in the background with the import script: it builds blendPath. Returns Blender's output.</summary>
    public static (bool Ok, string Log) Run(string blenderExe, string jsonPath, string blendPath, int timeoutMinutes = 30)
    {
        string script = Path.Combine(Path.GetDirectoryName(jsonPath)!, "bms_blender_import.py");
        var psi = new ProcessStartInfo(blenderExe)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "--background", "--python-exit-code", "3", "--python", script, "--", jsonPath, blendPath }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Blender did not start");
        var log = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (log) log.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (log) log.AppendLine(e.Data); };
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        if (!p.WaitForExit(timeoutMinutes * 60_000)) { try { p.Kill(true); } catch { } return (false, log + "\nBlender stopped: it took too long."); }
        p.WaitForExit();
        return (p.ExitCode == 0 && File.Exists(blendPath), log.ToString());
    }
}
