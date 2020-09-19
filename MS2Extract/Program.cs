using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MiscUtils;
using MiscUtils.IO;
using MS2Lib;
using Logger = MiscUtils.Logging.SimpleLogger;
using LogMode = MiscUtils.Logging.LogMode;

namespace MS2Extract
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

        private const int MinNumberArgs = 2;
        private const int OptionalNumberArgSyncMode = 3;
        private const int OptionalNumberArgLog = 4;

        // args
        private static string SourcePath;
        private static string DestinationPath;
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

            if (Directory.Exists(SourcePath))
            {
                Logger.Debug("Directory specified");
                Logger.Verbose($"Extracting all archives from \"{SourcePath}\" to \"{DestinationPath}\".");
                try
                {
                    await ExtractArchivesInDirectoryAsync(SourcePath, DestinationPath).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                    return;
                }
            }
            else
            {
                Logger.Debug("File Specified");
                Logger.Verbose($"Extracting archive \"{SourcePath}\" to \"{DestinationPath}\".");
                try
                {
                    await ExtractArchiveAsync(SourcePath, DestinationPath).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                    return;
                }
            }
        }

        private static async Task ExtractArchivesInDirectoryAsync(string sourcePath, string destinationPath)
        {
            if (!Directory.Exists(sourcePath))
            {
                throw new Exception($"Directory doesn't exist \"{sourcePath}\".");
            }

            foreach ((string headerFile, string dataFile) in GetFiles(sourcePath))
            {
                if (!sourcePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    sourcePath += Path.DirectorySeparatorChar;
                }

                string dstPath = Path.Combine(destinationPath, Path.GetDirectoryName(headerFile.Remove(sourcePath)));
                await CreateExtractArchiveAsync(dstPath, headerFile, dataFile).ConfigureAwait(false);
            }
        }

        private static Task ExtractArchiveAsync(string headerFile, string destinationPath)
        {
            headerFile = Path.ChangeExtension(headerFile, HeaderFileExtension);

            if (!File.Exists(headerFile))
            {
                throw new Exception($"File doesn't exist \"{headerFile}\".");
            }

            string dataFile = GetDataFileFromHeaderFile(headerFile);

            return CreateExtractArchiveAsync(destinationPath, headerFile, dataFile);
        }

        private static Task CreateExtractArchiveAsync(string destinationPath, string headerFile, string dataFile)
        {
            string dstPath = Path.Combine(destinationPath, Path.GetFileNameWithoutExtension(headerFile));
            Directory.CreateDirectory(dstPath);

            Logger.Verbose($"Starting extraction of: header \"{headerFile}\", data \"{dataFile}\".");
            if (ArgsSyncMode == SyncMode.Async)
            {
                return ExtractArchiveAsync(headerFile, dataFile, dstPath);
            }
            else //if (ArgsSyncMode == SyncMode.Sync)
            {
                return ExtractArchiveSync(headerFile, dataFile, dstPath);
            }
        }

        private static async Task ExtractArchiveAsync(string headerFile, string dataFile, string destinationPath)
        {
            using IMS2Archive archive = await MS2Archive.GetAndLoadArchiveAsync(headerFile, dataFile).ConfigureAwait(false);

            IEnumerable<Task> tasks = archive.Select(file =>
            {
                Logger.Info($"Extracting file \"{file.Name}\", \"{FileEx.FormatStorage(file.Header.Size.Size)}\". ({file.Header.Id}/{archive.Count})");

                return ExtractFileAsync(destinationPath, file);
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private static async Task ExtractArchiveSync(string headerFile, string dataFile, string destinationPath)
        {
            using IMS2Archive archive = await MS2Archive.GetAndLoadArchiveAsync(headerFile, dataFile).ConfigureAwait(false);

            foreach (var file in archive)
            {
                Logger.Info($"Extracting file \"{file.Name}\", \"{FileEx.FormatStorage(file.Header.Size.Size)}\". ({file.Header.Id}/{archive.Count})");
                await ExtractFileAsync(destinationPath, file).ConfigureAwait(false);
            }
        }

        private static async Task ExtractFileAsync(string destinationPath, IMS2File file)
        {
            if (String.IsNullOrWhiteSpace(file.Name))
            {
                Logger.Warning($"File number \"{file.Id}\", \"{FileEx.FormatStorage(file.Header.Size.Size)}\" has no name and will be ignored.");
                return;
            }

            string fileDestinationPath = Path.Combine(destinationPath, file.Name);

            using Stream stream = await file.GetStreamAsync().ConfigureAwait(false);

            await stream.CopyToAsync(fileDestinationPath).ConfigureAwait(false);
        }

        private static IEnumerable<(string headerFile, string dataFile)> GetFiles(string path)
        {
            string[] headerFiles = Directory.GetFiles(path, $"*.{HeaderFileExtension}", SearchOption.AllDirectories);

            for (int i = 0; i < headerFiles.Length; i++)
            {
                string headerFile = headerFiles[i];
                string dataFile = GetDataFileFromHeaderFile(headerFile);

                yield return (headerFile, dataFile);
            }
        }

        private static string GetDataFileFromHeaderFile(string headerFile)
        {
            string dataFile = Path.ChangeExtension(headerFile, DataFileExtension);

            if (!File.Exists(dataFile))
            {
                throw new Exception($"Matching data file for [{headerFile}] not found.");
            }

            return dataFile;
        }

        private static void DisplayArgsHelp()
        {
            var sb = new StringBuilder();

            sb.AppendLine();
            sb.AppendLine("MS2Extract Copyright (C) Miyu");
            sb.AppendLine("Description: ");
            sb.AppendLine("Extracts MapleStory2 archives in a given folder.");
            sb.AppendLine();
            sb.AppendLine("Usage: ");
            sb.AppendLine("MS2Extract.exe <source> <destination> [syncMode = Async] [logMode = Warning]");
            sb.AppendLine("<source> - either a directory to extract all archives, ");
            sb.AppendLine("\teither a specific archive.");
            sb.AppendLine("<destination> - the folder where all the files from");
            sb.AppendLine("\tthe archive will be extracted.");
            sb.AppendLine("<syncMode> - optional; \"Sync\" or \"Async\"");
            sb.AppendLine("\tor 0 or 1 respectively.");
            sb.AppendLine("\tAsync uses as much CPU as possible while");
            sb.AppendLine("\tSync will only use one thread.");
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
