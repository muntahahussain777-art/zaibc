using System;
using System.Configuration;
using System.IO;

namespace ZaibPetroleumService.Services
{
    /// <summary>
    /// Local AppData folder + SQLite file name (App.config). Defaults preserve original DiselPetrolPump paths.
    /// </summary>
    internal static class LocalAppStorage
    {
        public const string DefaultFolder = "DiselPetrolPump";
        public const string DefaultDatabaseFile = "DiselPetrolPump.db";

        public static string DataFolderName
        {
            get
            {
                string v = ConfigurationManager.AppSettings["LocalDataFolder"];
                return string.IsNullOrWhiteSpace(v) ? DefaultFolder : v.Trim();
            }
        }

        public static string DatabaseFileName
        {
            get
            {
                string v = ConfigurationManager.AppSettings["LocalDatabaseFileName"];
                return string.IsNullOrWhiteSpace(v) ? DefaultDatabaseFile : v.Trim();
            }
        }

        public static bool IsSupabaseSyncEnabled
        {
            get
            {
                string v = ConfigurationManager.AppSettings["SupabaseSyncEnabled"];
                if (string.IsNullOrWhiteSpace(v))
                    return true;
                v = v.Trim();
                return !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(v, "0", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string GetLocalDataDirectory()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                DataFolderName);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetDatabaseFilePath()
        {
            return Path.Combine(GetLocalDataDirectory(), DatabaseFileName);
        }
    }
}
