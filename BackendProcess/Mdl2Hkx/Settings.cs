// All conversion settings with the defaults of the Python reference v0.5, plus the 5 complexity presets
// measured in docs/04_SETTINGS_AND_PRESETS.md.
using System;

namespace Mdl2Hkx
{
    public sealed class ConversionSettings
    {
        // ---- material filter
        public bool SkipEmissive = true;
        public bool SkipGlass = false;
        public bool UseFoliageFilter = true;
        public string FoliageRegex = "leaf|frond|palmleaf|bush|grass|tuft|vine|ivy|fern|flower|petal|moss|weed|hedge|cable|wire|rope|netting|litter";
        public string SkipRegex = "";
        public int[] SkipMeshes = Array.Empty<int>();
        public double? MinY = null;

        // ---- resolution / trimming
        public double Voxel = 0.05;
        public double TrimRadius = 0.25;
        public double TrimKeep = 0.30;
        public double MinPieceSize = 0.30;
        public double Close = 0.0;            // exact mode only
        public int Thicken = 0;

        // ---- reachability
        public Vec3? Spawn = null;            // from the .SCENE SpawnPoint; null = largest floor
        public double Reach = 2.4;            // 0 = keep everything
        public double Margin = 0.6;
        public double FloodStep = 0.35;       // generous, to find floors (stairs with real risers count)
        public double WalkStep = 0.12;        // real Home step

        // ---- edge walls
        public double EdgeWallHeight = 0.30;  // 0 = off
        public double EdgeDrop = 0.40;
        public double EdgeThickness = 0.05;

        // ---- boxing
        public bool BigBlocks = false;
        public double Fill = 0.80;
        public double Headroom = 2.4;         // never below 2.3 (avatar sticks under 1.84 m)
        public double MinBox = 0.0;

        // ---- post
        public bool Snap = true;
        public double SnapTolerance = 0.02;
        public bool Prune = true;
        public double PruneDistance = 0.40;
        public double FloorTolerance = 0.15;  // 0 = off
        public double Seams = 0.30;           // 0 = off

        public const double AvatarHeight = 1.7;   // used for "standable" (free space above a floor cell)

        public ConversionSettings Clone() => (ConversionSettings)MemberwiseClone();

        /// <summary>Complexity 1 (fewest boxes) .. 5 (exact, largest). 2 is the in-game verified default.</summary>
        public static ConversionSettings Preset(int complexity)
        {
            var s = new ConversionSettings();
            switch (Math.Clamp(complexity, 1, 5))
            {
                case 1: s.BigBlocks = true; s.Voxel = 0.10; s.Fill = 0.55; break;   // Lowest   ~68 boxes on the apartment
                case 2: s.BigBlocks = true; s.Voxel = 0.08; s.Fill = 0.70; break;   // Simple   ~77 boxes (tested in Home)
                case 3: s.BigBlocks = true; s.Voxel = 0.06; s.Fill = 0.80; break;   // Balanced ~110 boxes
                case 4: s.BigBlocks = false; s.Voxel = 0.05; break;                  // Detailed ~1,000 boxes, exact fit
                case 5: s.BigBlocks = false; s.Voxel = 0.03; break;                  // Exact    ~3,000 boxes, slow
            }
            return s;
        }

        public static readonly string[] PresetNames = { "", "Lowest", "Simple (default)", "Balanced", "Detailed", "Exact" };
    }
}
