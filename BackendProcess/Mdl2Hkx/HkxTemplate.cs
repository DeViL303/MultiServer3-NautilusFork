// The one-body HKX template exported by the HDK (template/HKX_TEMPLATE.HKX in the handover), embedded in this assembly.
using System;
using System.IO;

namespace Mdl2Hkx
{
    public static class HkxTemplate
    {
        private static byte[]? _bytes;

        public static byte[] Bytes
        {
            get
            {
                if (_bytes != null) return _bytes;
                using var s = typeof(HkxTemplate).Assembly.GetManifestResourceStream("Mdl2Hkx.HKX_TEMPLATE.HKX")
                              ?? throw new InvalidOperationException("the HKX template resource is missing from Mdl2Hkx.dll");
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                _bytes = ms.ToArray();
                return _bytes;
            }
        }
    }
}
