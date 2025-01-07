using System;

namespace FlvMonitor.Library
{
    public static class VersionInfo
    {
        public const string AssemblyVersion = "0.2.0.0";
        public const string CopyrightYears = "2024 ~ 2025";
        public const string Website = "http://github.com/hotkidfamily";
        public const string Authors = "hotkidfamily";

        public static string DisplayVersion
        {
            get
            {
                Version ver = new (AssemblyVersion);
                return ver.Major + "." + ver.Minor + "." + ver.Revision;
            }
        }
    }
}
