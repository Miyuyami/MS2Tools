using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MiscUtils;
using MS2Lib;
using Logger = MiscUtils.Logging.SimpleLogger;
using LogMode = MiscUtils.Logging.LogMode;

namespace MS2Create
{
    public enum SyncMode
    {
        Sync = 0,
        Async = 1,
    }

    internal class Program
    {
        private const string HeaderFileExtension = "m2h";
        private const string DataFileExtension = "m2d";

        private const int MinNumberArgs = 4;
        private const int OptionalNumberArgSyncMode = 5;
        private const int OptionalNumberArgLog = 6;

        // args
        private static string SourcePath;
        private static string DestinationPath;
        private static string ArchiveName;
        private static MS2CryptoMode CryptoMode;
        private static LogMode? ArgsLogMode;
        private static SyncMode? ArgsSyncMode;

#if DEBUG
        private static readonly StreamWriter StreamWriter = new StreamWriter("output.log");
#endif

        static async Task Main(string[] commandLineArgs)
        {
#if DEBUG
            static void Out(string format, object[] args) => StreamWriter.WriteLine(args == null ? format : String.Format(format, args));
            Logger.Out = MiscUtils.Logging.DebugLogger.Out = Out;
            Logger.LoggingLevel = LogMode.Debug;
            ArgsSyncMode = SyncMode.Sync;
#else
            Logger.LoggingLevel = LogMode.Warning;
#endif

            await RunAsync(commandLineArgs).ConfigureAwait(false);

#if DEBUG
            StreamWriter.Dispose();
            Console.WriteLine("Press any key to close...");
            Console.ReadKey();
#endif
        }

        private static async Task RunAsync(string[] args)
        {
            if (!ParseArgs(args))
            {
                DisplayArgsHelp();
                return;
            }

            if (ArgsLogMode.HasValue)
            {
                Logger.LoggingLevel = ArgsLogMode.Value;
            }

            Logger.Verbose($"Archiving folder \"{SourcePath}\" to \"{DestinationPath}\".");
            try
            {
                await CreateArchiveAsync(SourcePath, DestinationPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                return;
            }
        }

        private static Task CreateArchiveAsync(string sourcePath, string destinationPath)
        {
            Directory.CreateDirectory(destinationPath);
            string dstArchive = Path.Combine(destinationPath, ArchiveName);
            string headerPath = Path.ChangeExtension(dstArchive, HeaderFileExtension);
            string dataPath = Path.ChangeExtension(dstArchive, DataFileExtension);
            Logger.Info($"Archiving folder \"{SourcePath}\" into \"{headerPath}\" and \"{dataPath}\"");

            if (ArgsSyncMode == SyncMode.Async)
            {
                return CreateArchiveAsync(sourcePath, headerPath, dataPath);
            }
            else //if (ArgsSyncMode == SyncMode.Sync)
            {
                return CreateArchiveSync(sourcePath, headerPath, dataPath);
            }
        }

        private static async Task CreateArchiveAsync(string sourcePath, string headerFilePath, string dataFilePath)
        {
            if (!Directory.Exists(sourcePath))
            {
                throw new Exception($"Directory doesn't exist \"{sourcePath}\".");
            }

            var filePaths = GetFilesRelative(sourcePath);
            MS2File[] files = new MS2File[filePaths.Length];
            var tasks = new Task[filePaths.Length];
            IMS2Archive archive = new MS2Archive(Repositories.Repos[CryptoMode]);

            for (uint i = 0; i < filePaths.Length; i++)
            {
                uint ic = i;
                tasks[i] = Task.Run(() => AddAndCreateFileToArchive(archive, filePaths, ic));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            await archive.SaveAsync(headerFilePath, dataFilePath, true, f => GetCompressionTypeFromFileExtension(f.Info.Path, CompressionType.Zlib)).ConfigureAwait(false);
        }

        private static void AddAndCreateFileToArchive(IMS2Archive archive, (string fullPath, string relativePath)[] filePaths, uint index)
        {
            var (filePath, relativePath) = filePaths[index];

            uint id = index + 1;
            FileStream fsFile = File.OpenRead(filePath);
            IMS2FileInfo info = new MS2FileInfo(id.ToString(), relativePath);
            IMS2FileHeader header = new MS2FileHeader(fsFile.Length, id, 0, GetCompressionTypeFromFileExtension(filePath));
            IMS2File file = new MS2File(archive, fsFile, info, header, false);

            archive.Add(file);
        }

        private static async Task CreateArchiveSync(string sourcePath, string headerFilePath, string dataFilePath)
        {
            if (!Directory.Exists(sourcePath))
            {
                throw new Exception($"Directory doesn't exist \"{sourcePath}\".");
            }

            var filePaths = GetFilesRelative(sourcePath);
            MS2File[] files = new MS2File[filePaths.Length];
            IMS2Archive archive = new MS2Archive(Repositories.Repos[CryptoMode]);

            for (uint i = 0; i < filePaths.Length; i++)
            {
                AddAndCreateFileToArchive(archive, filePaths, i);
            }

            await archive.SaveAsync(headerFilePath, dataFilePath, false, f => GetCompressionTypeFromFileExtension(f.Info.Path, CompressionType.Zlib)).ConfigureAwait(false);
        }

        private static (string FullPath, string RelativePath)[] GetFilesRelative(string path)
        {
            if (!path.EndsWith(@"\"))
            {
                path += @"\";
            }

            string[] files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
            var result = new (string FullPath, string RelativePath)[files.Length];

            for (int i = 0; i < files.Length; i++)
            {
                result[i] = (files[i], files[i].Remove(path));
            }

            return result;
        }

        private static CompressionType GetCompressionTypeFromFileExtension(string filePath, CompressionType defaultCompressionType = CompressionType.None) =>
            (Path.GetExtension(filePath)) switch
            {
                ".png" => CompressionType.Png,
                ".usm" => CompressionType.Usm,
                ".zlib" => CompressionType.Zlib,
                _ => defaultCompressionType,
            };

        private static void DisplayArgsHelp()
        {
            var sb = new StringBuilder();

            sb.AppendLine();
            sb.AppendLine("MS2Create Copyright (C) 2017-2018 Miyu");
            sb.AppendLine("Description: ");
            sb.AppendLine("Creates a MapleStory2 archive from a given folder.");
            sb.AppendLine();
            sb.AppendLine("Usage: ");
            sb.AppendLine("MS2Create.exe <source> <destination> <archive name> <mode> [syncMode = Async] [logMode = Warning]");
            sb.AppendLine("<source> - the folder to be archived.");
            sb.AppendLine("<destination> - the folder where the archive will be created.");
            sb.AppendLine("<archive name> - the name of the resulting archive.");
            sb.AppendLine("<mode> - the mode to use to encrypt the archive.");
            sb.AppendLine("List of available modes: MS2F, NS2F, OS2F, PS2F");
            sb.AppendLine("<syncMode> - optional; \"Sync\" or \"Async\"");
            sb.AppendLine("or 0 or 1 respectively.");
            sb.AppendLine("Async uses as much CPU as possible while");
            sb.AppendLine("Sync will only use one thread.");
            sb.AppendLine("<logMode> - optional; Debug, Verbose, Info, Warning or Error");

            Console.WriteLine(sb.ToString());
        }

        private static bool ParseArgs(string[] args)
        {
            if (args.Length < MinNumberArgs)
            {
                Logger.Error("not enough args");
                return false;
            }

            if (args.Any(s => String.IsNullOrWhiteSpace(s)))
            {
                Logger.Error("one or more of the args is not valid");
                return false;
            }

            SourcePath = Path.GetFullPath(args[0]);
            DestinationPath = Path.GetFullPath(args[1]);
            ArchiveName = args[2];
            CryptoMode = (MS2CryptoMode)Enum.Parse(typeof(MS2CryptoMode), args[3]);

            if (args.Length >= OptionalNumberArgSyncMode)
            {
                ArgsSyncMode = (SyncMode)Enum.Parse(typeof(SyncMode), args[OptionalNumberArgSyncMode - 1]);

                if (args.Length >= OptionalNumberArgLog)
                {
                    ArgsLogMode = (LogMode)Enum.Parse(typeof(LogMode), args[OptionalNumberArgLog - 1]);
                }
            }

            return true;
        }
    }
}
