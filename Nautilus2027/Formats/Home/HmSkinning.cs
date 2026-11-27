#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

// MDL skinning data: IN.blendIndices = 4 joint indices (UByte, or Short) into the skeleton named by the MDL header,
// IN.blendWeights = 3 weights packed 11:11:10 signed-normalised in a u32 (or 2-3 plain components); the last weight is
// 1 - the sum of the others. From the HOME_DEV_KIT (ws/mdlb/src/08.js) and Hub::HomSubMeshDef in the HDK eboot.
namespace Nautilus2027.Formats.Home
{
    public static class HmSkinning
    {
        public static readonly uint HBlendIndices = HmCore.HashName("IN.blendIndices");
        public static readonly uint HBlendWeights = HmCore.HashName("IN.blendWeights");

        public static bool IsSkinned(HmMesh me) => me.Fields.Any(f => f.Name == HBlendIndices);

        /// <summary>Packed 11:11:10 weights -> 4 weights per vertex.</summary>
        public static List<double[]> DecodePackedWeights(HmMesh me, HmField f)
        {
            var outL = new List<double[]>(me.VertexCount);
            for (int i = 0; i < me.VertexCount; i++)
            {
                uint v = HmCoreBE.U32(me.Vertices, i * me.Stride + f.Offset);
                int sx = (int)(v << 21) >> 21, sy = (int)(v << 10) >> 21, sz = (int)v >> 22;
                double w0 = sx / 1023.0, w1 = sy / 1023.0, w2 = sz / 511.0;
                outL.Add(new[] { w0, w1, w2, Math.Max(0, 1 - w0 - w1 - w2) });
            }
            return outL;
        }

        public static uint PackWeights(double w0, double w1, double w2)
        {
            uint C(double x, int max) => (uint)Math.Max(0, Math.Min(max, HmCore.JsRound(x * max)));
            return (C(w0, 1023) & 0x7FF) | ((C(w1, 1023) & 0x7FF) << 11) | ((C(w2, 511) & 0x3FF) << 22);
        }

        /// <summary>Joint indices and weights (4 per vertex) of a skinned element, or null.</summary>
        public static (int[] J, float[] W)? Decode(HmMesh me)
        {
            var fi = me.Fields.FirstOrDefault(f => f.Name == HBlendIndices);
            if (fi == null) return null;
            var fw = me.Fields.FirstOrDefault(f => f.Name == HBlendWeights);
            int n = me.VertexCount;
            var J = new int[n * 4];
            var W = new float[n * 4];
            var idx = HmCore.MeshAttribute(me, HBlendIndices);
            List<double[]> wts = null;
            if (fw != null) wts = fw.Type == HmCore.StPacked && fw.Count == 1 ? DecodePackedWeights(me, fw) : HmCore.MeshAttribute(me, HBlendWeights);
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < 4; k++) J[4 * i + k] = idx != null && k < idx[i].Length ? (int)idx[i][k] : (idx != null && idx[i].Length > 0 ? (int)idx[i][idx[i].Length - 1] : 0);
                var w = new double[4];
                if (wts == null) w[0] = 1;
                else if (wts[i].Length >= 4) Array.Copy(wts[i], w, 4);
                else
                {
                    int used = Math.Min(3, wts[i].Length);
                    for (int k = 0; k < used; k++) w[k] = wts[i][k];
                    w[used] = Math.Max(0, 1 - w.Take(used).Sum());
                }
                double s = w.Sum();
                for (int k = 0; k < 4; k++) W[4 * i + k] = (float)(s > 0 ? Math.Max(0, w[k]) / s : (k == 0 ? 1 : 0));
            }
            return (J, W);
        }
    }
}
