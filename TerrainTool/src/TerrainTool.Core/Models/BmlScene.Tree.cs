namespace TerrainTool.Core.Models;

/// <summary>
/// The structure of a LOD as a tree (for the export to Blender): DOFs, switches and slots with their children, and every
/// primitive with its vertices in the model's coordinates at rest (BMS coordinates, every switch branch included).
/// </summary>
public sealed partial class BmlScene
{
    public sealed class TreeNode
    {
        /// <summary>root, dof, switch, slot, mesh</summary>
        public string Type = "root";
        public DofInfo? Dof;
        public SwitchInfo? Switch;
        public SlotInfo? Slot;
        /// <summary>Frame of this node in the model at rest (3x4, row by row, p' = M p), BMS coordinates.</summary>
        public double[] Frame = Identity();
        public List<TreeNode> Children { get; } = new();
        // mesh
        public int Material = -1;
        public string MaterialName = "";
        public string? Texture;
        public bool Glass, Light;
        public uint Color = 0xFFFFFFFF;
        public float[] P = Array.Empty<float>(), N = Array.Empty<float>(), Uv = Array.Empty<float>();
        public int[] I = Array.Empty<int>();
    }

    /// <summary>Materials of the loaded texture set: name, texture (old models), glass, emissive.</summary>
    public IReadOnlyList<(string Name, string? Texture, bool Glass, bool Emissive)> Materials => _mats.Select(m => (m.Name, m.Tex, m.Glass, m.Skip)).ToList();

    private static double Det(float[] r) =>
        r[0] * (r[4] * r[8] - r[5] * r[7]) - r[1] * (r[3] * r[8] - r[5] * r[6]) + r[2] * (r[3] * r[7] - r[4] * r[6]);

    public TreeNode Tree()
    {
        var root = new TreeNode();
        var stack = new Stack<(TreeNode Node, double[] M)>();
        var cur = root; var M = Identity();
        var b = _b;
        foreach (var n in _nodes)
        {
            switch (n.Type)
            {
                case 2:
                {
                    // the frame as written in the file: a rotation a little off (the viewer takes it as none) is kept as it is
                    var d = n.Dof!;
                    var f = d.Kind == DofKind.Rotate && !d.ValidMatrix && Finite(d.R) && Det(d.R) > 0.5
                        ? Mul(M, Local(d.R, Finite(d.T) ? d.T : ZeroT))
                        : Mul(M, DofLocal(d, 0));
                    var t = new TreeNode { Type = "dof", Dof = n.Dof, Frame = f };
                    cur.Children.Add(t); stack.Push((cur, M)); cur = t; M = f;
                    break;
                }
                case 4:
                {
                    var t = new TreeNode { Type = "switch", Switch = n.Switch, Frame = M };
                    cur.Children.Add(t); stack.Push((cur, M)); cur = t;
                    break;
                }
                case 6:
                {
                    var t = new TreeNode { Type = "slot", Slot = n.Slot, Frame = Mul(M, Local(IsRotation(n.Slot!.R) ? n.Slot.R : IdentityR, n.Slot.Origin)) };
                    cur.Children.Add(t); stack.Push((cur, M)); cur = t;
                    break;
                }
                case 3: case 5: case 7:
                    if (stack.Count > 0) (cur, M) = stack.Pop();
                    break;
                case 1:
                {
                    if (n.VertexCount <= 0) break;
                    var mat = n.Material >= 0 && n.Material < _mats.Count ? _mats[n.Material] : (null, false, false, "");
                    bool glow = (Version == 1 && n.Material < 0 && n.VertexSize != 36) || mat.Skip || n.VertexSize == 44;
                    var t = new TreeNode { Type = "mesh", Material = n.Material, MaterialName = mat.Item4, Texture = mat.Item1, Glass = mat.Item2, Light = glow };
                    if (n.VertexSize == 40 || n.VertexSize == 44) t.Color = BitConverter.ToUInt32(b, Math.Min(b.Length - 4, _vbAt + n.VertexOffset + 24));
                    int uvAt = Version == 1 ? 28 : n.VertexSize >= 48 ? 36 : 28;
                    bool hasNormal = n.VertexSize >= 24;
                    var P = new List<float>(n.VertexCount * 3); var N = new List<float>(n.VertexCount * 3); var T = new List<float>(n.VertexCount * 2);
                    for (int v = 0; v < n.VertexCount; v++)
                    {
                        int p = _vbAt + n.VertexOffset + v * n.VertexSize;
                        if (p + n.VertexSize > _vbAt + _vbSize) break;
                        double x = BitConverter.ToSingle(b, p), y = BitConverter.ToSingle(b, p + 4), z = BitConverter.ToSingle(b, p + 8);
                        P.Add((float)(M[0] * x + M[1] * y + M[2] * z + M[3])); P.Add((float)(M[4] * x + M[5] * y + M[6] * z + M[7])); P.Add((float)(M[8] * x + M[9] * y + M[10] * z + M[11]));
                        if (hasNormal)
                        {
                            double nx = BitConverter.ToSingle(b, p + 12), ny = BitConverter.ToSingle(b, p + 16), nz = BitConverter.ToSingle(b, p + 20);
                            N.Add((float)(M[0] * nx + M[1] * ny + M[2] * nz)); N.Add((float)(M[4] * nx + M[5] * ny + M[6] * nz)); N.Add((float)(M[8] * nx + M[9] * ny + M[10] * nz));
                        }
                        else { N.Add(0); N.Add(1); N.Add(0); }
                        if (uvAt + 8 <= n.VertexSize) { T.Add(BitConverter.ToSingle(b, p + uvAt)); T.Add(BitConverter.ToSingle(b, p + uvAt + 4)); }
                        else { T.Add(0); T.Add(0); }
                    }
                    int added = P.Count / 3;
                    var I = new List<int>();
                    if (n.IndexCount > 0 && _ibSize > 0)
                    {
                        int isz = _ibFormat == 2 ? 4 : 2;
                        int vbase = n.VertexSize > 0 ? n.VertexOffset / n.VertexSize : 0;
                        int ib0 = BmlModel.IndexBase(k => Idx(isz, k), n.StartIndex, n.IndexCount, added, vbase, n.VertexStartIndex);
                        for (int i = 0; i + 2 < n.IndexCount; i += 3)
                        {
                            int a = Idx(isz, n.StartIndex + i) - ib0, c = Idx(isz, n.StartIndex + i + 1) - ib0, d = Idx(isz, n.StartIndex + i + 2) - ib0;
                            if ((uint)a >= added || (uint)c >= added || (uint)d >= added) continue;
                            I.Add(a); I.Add(c); I.Add(d);
                        }
                    }
                    else
                        for (int i = 0; i + 2 < added; i += 3) { I.Add(i); I.Add(i + 1); I.Add(i + 2); }
                    if (I.Count == 0) break;
                    t.P = P.ToArray(); t.N = N.ToArray(); t.Uv = T.ToArray(); t.I = I.ToArray();
                    cur.Children.Add(t);
                    break;
                }
            }
        }
        return root;
    }
}
