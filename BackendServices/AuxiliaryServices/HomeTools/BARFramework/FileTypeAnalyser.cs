using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace HomeTools.BARFramework
{
    public class FileTypeAnalyser
    {
        public static FileTypeAnalyser Instance
        {
            get
            {
                if (m_instance == null)
                    m_instance = new FileTypeAnalyser();
                return m_instance;
            }
        }

        private FileTypeAnalyser()
        {
            m_FileTypeExtensions = new Dictionary<HomeFileType, string>();
            RegisterFileTypes();
        }

        public string GetRegisteredExtension(HomeFileType type)
        {
            return m_FileTypeExtensions[type];
        }

        private void RegisterFileTypes()
        {

            m_FileTypeExtensions[HomeFileType.Unknown] = string.Empty;
        }

        public HomeFileType Analyse(Stream inStream)
        {
            return HomeFileType.Unknown;  // Immediate return, no processing
        }


        private static FileTypeAnalyser m_instance;

        private Dictionary<HomeFileType, string> m_FileTypeExtensions;
    }
}