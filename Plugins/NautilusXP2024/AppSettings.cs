using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NautilusXP2024
{
    public class AppSettings
    {
        public string CdsEncryptOutputDirectory { get; set; }
        public string CdsDecryptOutputDirectory { get; set; }
        public string BarSdatSharcOutputDirectory { get; set; }
        public string MappedOutputDirectory { get; set; }
        public string HcdbOutputDirectory { get; set; }
        public string SqlOutputDirectory { get; set; }
        public string TicketListOutputDirectory { get; set; }
        public string LuacOutputDirectory { get; set; }
        public string LuaOutputDirectory { get; set; }
        public string AudioOutputDirectory { get; set; }

        public string VideoOutputDirectory { get; set; }
        public int CpuPercentage { get; set; }
        public int MappingThreads { get; set; }
        public string ThemeColor { get; set; }
        public OverwriteBehavior FileOverwriteBehavior { get; set; }
        public SaveDebugLog SaveDebugLogToggle { get; set; }
        public ArchiveTypeSetting ArchiveTypeSettingRem { get; set; }
        public ArchiveMapperSetting ArchiveMapperSettingRem { get; set; }
        public RememberLastTabUsed LastTabUsed { get; set; }

        // Constructor to initialize default values
        public AppSettings()
        {
            // Set default values
            CdsEncryptOutputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "CDSEncrypt");
            CdsDecryptOutputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "CDSDecrypt");
            BarSdatSharcOutputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "Archive");
            MappedOutputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "Mapped");
            HcdbOutputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "HCDB");
            SqlOutputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "SQL");
            TicketListOutputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "LST");
            LuacOutputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "LUAC");
            LuaOutputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "LUA");
            AudioOutputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "Audio");
            VideoOutputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "Video");
            CpuPercentage = 50;
            MappingThreads = 12;
            ThemeColor = "#fc030f"; // Default color as a string
            FileOverwriteBehavior = OverwriteBehavior.Rename;
            SaveDebugLogToggle = SaveDebugLog.True;
            ArchiveTypeSettingRem = ArchiveTypeSetting.SDAT;
            ArchiveMapperSettingRem = ArchiveMapperSetting.EXP;
            LastTabUsed = RememberLastTabUsed.ArchiveTool;
        }
    }


}
