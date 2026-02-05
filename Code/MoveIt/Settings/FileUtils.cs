// File: Settings/FileUtils.cs
// Purpose: Small file helpers for Move It (open mod folders, bundle logs/settings into a zip).

using Colossal.PSI.Environment;
using MoveIt.Tool;
using QCommonLib;
using System;
using System.IO;
using System.IO.Compression;

namespace MoveIt.Settings
{
    public class FileUtils
    {
        public static string ModsFolder => Path.Combine(EnvPath.kUserDataPath, "Mods");
        public static string ModsDataFolder => Path.Combine(EnvPath.kUserDataPath, "ModsData");

        public static bool GooeeModsFolderExists => Directory.Exists(Path.Combine(ModsFolder, "Gooee"));
        public static bool GooeeModsDataFolderExists => Directory.Exists(Path.Combine(ModsDataFolder, "Gooee"));

        public static bool GooeeBothFoldersExist => GooeeModsFolderExists && GooeeModsDataFolderExists;

        // Hide warning if neither folder exists.
        public static bool HideGooeeWarning => !GooeeModsFolderExists && !GooeeModsDataFolderExists;

        public static bool OpenLocalModsFolder()
        {
            // Open both folders if present. No exception should occur if one is missing.
            if (GooeeModsFolderExists) Colossal.RemoteProcess.OpenFolder(ModsFolder);
            if (GooeeModsDataFolderExists) Colossal.RemoteProcess.OpenFolder(ModsDataFolder);
            return true;
        }

        internal static void SaveLogsToDesktop()
        {
            // Zip name is timestamped to avoid collisions.
            string timestamp = $"{DateTime.Now:yyyy-MM-dd_HH_mm_ss}";
            string logTime = QLoggerBase.GetFormattedTimeNow();
            string pathDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            string archiveFile = Path.Combine(pathDesktop, $"MoveIt_Logs_{timestamp}.zip");

            string pathAppData = EnvPath.kUserDataPath;
            string pathLogs = Path.Combine(pathAppData, "Logs");

            // Root files live directly under user data path (not necessarily inside /Logs).
            string[] rootFiles = new string[5] { "Player.log", "Player-prev.log", "MoveIt.coc", "Settings.coc", "UserState.coc" };

            // Guard: log system can be null during early load/unload.
            if (MoveItToolSystem.Log is not null)
            {
                MoveItToolSystem.Log.Info($"Saving log files from {pathAppData} at {logTime}");
            }

            try
            {
                if (!Directory.Exists(pathLogs))
                {
                    if (MoveItToolSystem.Log is not null)
                    {
                        MoveItToolSystem.Log.Info($"Log folder {pathLogs} not found.");
                    }
                    return;
                }

                // Overwrite existing archive if timestamp collisions or manual rename occurred.
                if (File.Exists(archiveFile))
                {
                    File.Delete(archiveFile);
                }

                // Create zip from Logs folder (includes nested structure).
                ZipFile.CreateFromDirectory(pathLogs, archiveFile, CompressionLevel.Optimal, true);

                // Append root files into the same zip.
                using ZipArchive archive = ZipFile.Open(archiveFile, ZipArchiveMode.Update);

                foreach (string file in rootFiles)
                {
                    string logFile = Path.Combine(pathAppData, file);

                    if (!File.Exists(logFile))
                    {
                        continue;
                    }

                    // Copy via temp file to reduce sharing/lock issues.
                    string tmpFile = Path.Combine(pathAppData, $"MIT_Log_{Guid.NewGuid():N}.tmp");

                    try
                    {
                        File.Copy(logFile, tmpFile, true);
                        archive.CreateEntryFromFile(tmpFile, file);
                    }
                    finally
                    {
                        if (File.Exists(tmpFile))
                        {
                            File.Delete(tmpFile);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Keep original behavior: log the error message, do not throw.
                if (MoveItToolSystem.Log is not null)
                {
                    MoveItToolSystem.Log.Error(ex.Message);
                }
            }
        }
    }
}
