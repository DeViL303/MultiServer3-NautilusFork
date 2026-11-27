using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nautilus2027
{
    public class AppSettings
    {
        // ------------------------------------------------------------------ output folders
        // One folder per tab (set in Settings); each tool of the tab writes to its own folder inside it.

        private static string DefaultRoot(string tab) => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", tab);
        private static string Under(string root, string tab, string tool) => Path.Combine(string.IsNullOrWhiteSpace(root) ? DefaultRoot(tab) : root.Trim(), tool);

        public string ArchiveOutputRoot { get; set; }
        public string CdsOutputRoot { get; set; }
        public string HcdbOutputRoot { get; set; }
        public string LuaOutputRoot { get; set; }
        public string ModelOutputRoot { get; set; }
        public string AnimationOutputRoot { get; set; }
        public string AtmosOutputRoot { get; set; }
        public string EfxOutputRoot { get; set; }
        public string AgfRigOutputRoot { get; set; }
        public string MediaOutputRoot { get; set; }
        public string CatalogueOutputRoot { get; set; }
        public string CdnAuditOutputRoot { get; set; }
        public string SceneListOutputRoot { get; set; }

        // the tools' folders (worked out from the tab folders, not saved)
        public string BarSdatSharcOutputDirectory => Under(ArchiveOutputRoot, "BAR_SDAT_SHARC", "Created");
        public string MappedOutputDirectory => Under(ArchiveOutputRoot, "BAR_SDAT_SHARC", "Unpacked");
        public string CdsEncryptOutputDirectory => Under(CdsOutputRoot, "CDS", "Encrypted");
        public string CdsDecryptOutputDirectory => Under(CdsOutputRoot, "CDS", "Decrypted");
        public string HcdbOutputDirectory => Under(HcdbOutputRoot, "HCDB_SQL", "HCDB");
        public string SqlOutputDirectory => Under(HcdbOutputRoot, "HCDB_SQL", "SQL");
        public string TicketListOutputDirectory => Under(HcdbOutputRoot, "HCDB_SQL", "LST");
        public string LuacOutputDirectory => Under(LuaOutputRoot, "LUA_LUAC", "LUAC");
        public string LuaOutputDirectory => Under(LuaOutputRoot, "LUA_LUAC", "LUA");
        public string ModelToolDirectory => Under(ModelOutputRoot, "MDL_HKX", "MDL");
        public string CollisionToolDirectory => Under(ModelOutputRoot, "MDL_HKX", "HKX");
        public string AnimationToolDirectory => Under(AnimationOutputRoot, "ANI_SKN", "ANI");
        public string SkeletonToolDirectory => Under(AnimationOutputRoot, "ANI_SKN", "SKN");
        public string AtmosToolDirectory => Under(AtmosOutputRoot, "ATMOS_CDATA", "ATMOS");
        public string CdataToolDirectory => Under(AtmosOutputRoot, "ATMOS_CDATA", "CDATA");
        public string EfxToolDirectory => Under(EfxOutputRoot, "EFX_PROBE", "EFX");
        public string ProbeToolDirectory => Under(EfxOutputRoot, "EFX_PROBE", "PROBE");
        public string AgfToolDirectory => Under(AgfRigOutputRoot, "AGF_RIG", "AGF");
        public string RigToolDirectory => Under(AgfRigOutputRoot, "AGF_RIG", "RIG");
        public string VideoOutputDirectory => Under(MediaOutputRoot, "Media", "Video");
        public string AudioOutputDirectory => Under(MediaOutputRoot, "Media", "Audio");
        public string CatalogueDownloadsDirectory => Under(CatalogueOutputRoot, "Catalogue", "Downloads");
        public string CdnAuditDirectory => Under(CdnAuditOutputRoot, "CDN_Audit", "Audit");
        public string SceneListDeployDirectory => Under(SceneListOutputRoot, "SceneList", "ToDeploy");


        public string ThemeColor { get; set; }

        public string SceneListSavePath { get; set; }

        public string SceneListPathURL { get; set; }

        public string TSSURL { get; set; }

        public string TSSeditorSavePath { get; set; }
        public OverwriteBehavior FileOverwriteBehavior { get; set; }
        public SaveDebugLog SaveDebugLogToggle { get; set; }
        public bool LiteCatalogueEnabled { get; set; }
        public ArchiveTypeSetting ArchiveTypeSettingRem { get; set; }
        public ArchiveMapperSetting ArchiveMapperSettingRem { get; set; }
        public RememberLastTabUsed LastTabUsed { get; set; }
        /// <summary>Show the online psho.me catalogue (true) or the local copy in dependencies/psho.me (false).
        /// Saved under a new name so it starts on for everyone, as the old IsOfflineMode setting defaulted to off.</summary>
        public bool UseOnlineCatalogue { get; set; }

        /// <summary>Interface size in percent (0 = automatic: smaller on high-DPI screens such as 4K at 200% or more).</summary>
        public int UiScalePercent { get; set; }


        // Constructor to initialize default values
        public AppSettings()
        {
            // Set default values
            ArchiveOutputRoot = DefaultRoot("BAR_SDAT_SHARC");
            CdsOutputRoot = DefaultRoot("CDS");
            HcdbOutputRoot = DefaultRoot("HCDB_SQL");
            LuaOutputRoot = DefaultRoot("LUA_LUAC");
            ModelOutputRoot = DefaultRoot("MDL_HKX");
            AnimationOutputRoot = DefaultRoot("ANI_SKN");
            AtmosOutputRoot = DefaultRoot("ATMOS_CDATA");
            EfxOutputRoot = DefaultRoot("EFX_PROBE");
            AgfRigOutputRoot = DefaultRoot("AGF_RIG");
            MediaOutputRoot = DefaultRoot("Media");
            CatalogueOutputRoot = DefaultRoot("Catalogue");
            CdnAuditOutputRoot = DefaultRoot("CDN_Audit");
            SceneListOutputRoot = DefaultRoot("SceneList");
            SceneListSavePath = "C:\\PSMultiserver\\static\\wwwroot\\Environments\\"; 
            SceneListPathURL = "https://pshomeologylab.net/Environments/SceneList_dec.xml"; 
            TSSURL = "https://pshomeologylab.net/tss/coreHztFmpQrx0002_en-US.xml"; 
            TSSeditorSavePath = "C:\\PSMultiserver\\static\\wwwroot\\"; 
            ThemeColor = "#fc030f"; // Default color as a string
            SaveDebugLogToggle = SaveDebugLog.True;
            LiteCatalogueEnabled = true;
            ArchiveTypeSettingRem = ArchiveTypeSetting.SDAT;
            ArchiveMapperSettingRem = ArchiveMapperSetting.EXP;
            LastTabUsed = RememberLastTabUsed.ArchiveTool;
            UseOnlineCatalogue = true; // on by default
        }
    }


}
