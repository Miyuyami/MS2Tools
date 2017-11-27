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
    internal class Program
    {
        private const string HeaderFileExtension = "m2h";
        private const string DataFileExtension = "m2d";

        private const int MinArgsLength = 2;

        // args
        private static string SourcePath;
        private static string DestinationPath;
        private static LogMode? ArgsLogMode;

#if DEBUG
        private static readonly StreamWriter StreamWriter = new StreamWriter("output.log");
#endif

        static async Task Main(string[] commandLineArgs)
        {
#if DEBUG
            Action<string, object[]> Out = (format, args) => StreamWriter.WriteLine(args == null ? format : String.Format(format, args));
            Logger.Out = MiscUtils.Logging.DebugLogger.Out = Out;
            Logger.LoggingLevel = LogMode.Debug;
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
                if (!sourcePath.EndsWith(@"\"))
                {
                    sourcePath += @"\";
                }

                string dstPath = Path.Combine(destinationPath, Path.GetDirectoryName(headerFile.Replace(sourcePath, String.Empty)));
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
            return ExtractArchiveAsync(headerFile, dataFile, dstPath);
        }

        private static async Task ExtractArchiveAsync(string headerFile, string dataFile, string destinationPath)
        {
            using (MS2Archive archive = await MS2Archive.Load(headerFile, dataFile).ConfigureAwait(false))
            {
                List<MS2File> files = archive.Files;
#if DEBUG
                for (int i = 0; i < files.Count; i++)
                {
                    await ExtractFileAsync(destinationPath, files, i).ConfigureAwait(false);
                }
#else
                Task[] tasks = new Task[files.Count];
                for (int i = 0; i < files.Count; i++)
                {
                    tasks[i] = ExtractFileAsync(destinationPath, files, i);
                }
                await Task.WhenAll(tasks).ConfigureAwait(false);
#endif
            }
        }

        private static async Task ExtractFileAsync(string destinationPath, List<MS2File> files, int i)
        {
            MS2File file = files[i];

            string fileDestinationPath = Path.Combine(destinationPath, file.Name);

            Logger.Info($"Extracting file \"{file.Name}\", \"{FileEx.FormatStorage(file.Header.Size)}\". ({file.Header.Id}/{files.Count})");

            if (file.Name == String.Empty)
            {
                Logger.Warning($"File number \"{file.Id}\", \"{FileEx.FormatStorage(file.Header.Size)}\" has no name and will be ignored.");
                return;
            }
            
            (Stream stream, bool shouldDispose) = await file.GetDecryptedStreamAsync().ConfigureAwait(false);

            try
            {
                await stream.CopyToAsync(fileDestinationPath).ConfigureAwait(false);
            }
            finally
            {
                if (shouldDispose)
                {
                    stream.Dispose();
                }
            }
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
            sb.AppendLine("MS2Extract Copyright (C) 2017-2018 Miyu");
            sb.AppendLine("Description: ");
            sb.AppendLine("Extracts MapleStory2 archives in a given folder.");
            sb.AppendLine();
            sb.AppendLine("Usage: ");
            sb.AppendLine("MS2Extract.exe <source> <destination>");
            sb.AppendLine("<source> - either a directory to extract all archives, ");
            sb.AppendLine("either a specific archive");
            sb.AppendLine("<destination> - the folder where all the files from");
            sb.AppendLine("the archive will be extracted");

            Console.WriteLine(sb.ToString());
        }

        private static bool ParseArgs(string[] args)
        {
            if (args.Length < MinArgsLength)
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

            if (args.Length > MinArgsLength)
            {
                ArgsLogMode = (LogMode)Enum.Parse(typeof(LogMode), args[2]);
            }

            return true;
        }
    }
}
