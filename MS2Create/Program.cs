using System;
using System.Collections.Generic;
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
    internal class Program
    {
        private const string HeaderFileExtension = "m2h";
        private const string DataFileExtension = "m2d";

        private const int MinArgsLength = 4;

        // args
        private static string SourcePath;
        private static string DestinationPath;
        private static string ArchiveName;
        private static MS2CryptoMode CryptoMode;
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
            string dstArchive = Path.Combine(destinationPath, ArchiveName);
            return CreateArchiveAsync(sourcePath, Path.Combine(dstArchive, HeaderFileExtension), Path.Combine(dstArchive, DataFileExtension));
        }

        private static Task CreateArchiveAsync(string sourcePath, string headerFilePath, string dataFilePath)
        {
            if (!Directory.Exists(sourcePath))
            {
                throw new Exception($"Directory doesn't exist \"{sourcePath}\".");
            }

            var filePaths = GetFilesRelative(sourcePath);
            List<MS2File> files = new List<MS2File>(filePaths.Length);
            for (uint i = 0; i < filePaths.Length; i++)
            {
                var (filePath, relativePath) = filePaths[i];
                files.Add(MS2File.Create(i + 1u, relativePath, CompressionType.Zlib, CryptoMode, filePath));
            }

            return MS2Archive.Save(CryptoMode, files, headerFilePath, dataFilePath, RunMode.Async2);
        }

        private static (string FullPath, string RelativePath)[] GetFilesRelative(string path)
        {
            if (!path.EndsWith(@"\"))
            {
                path += @"\";
            }

            string[] files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
            var result = new(string FullPath, string RelativePath)[files.Length];

            for (int i = 0; i < files.Length; i++)
            {
                result[i] = (files[i], files[i].Remove(path));
            }

            return result;
        }

        private static string GetDataFileFromHeaderFile(string headerFile)
        {
            string dataFile = Path.ChangeExtension(headerFile, DataFileExtension);

            return dataFile;
        }

        private static void DisplayArgsHelp()
        {
            var sb = new StringBuilder();

            sb.AppendLine();
            sb.AppendLine("MS2Create Copyright (C) 2017-2018 Miyu");
            sb.AppendLine("Description: ");
            sb.AppendLine("Creates a MapleStory2 archive from a given folder.");
            sb.AppendLine();
            sb.AppendLine("Usage: ");
            sb.AppendLine("MS2Create.exe <source> <destination> <archive name> <mode>");
            sb.AppendLine("<source> - the folder to be archived.");
            sb.AppendLine("<destination> - the folder where the archive will be created.");
            sb.AppendLine("<archive name> - the name of the resulting archive.");
            sb.AppendLine("<mode> - the mode to use to encrypt the archive.");
            sb.AppendLine("List of available modes: MS2F, NS2F, OS2F, PS2F");

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
            ArchiveName = args[2];
            CryptoMode = (MS2CryptoMode)Enum.Parse(typeof(MS2CryptoMode), args[3]);

            if (args.Length > MinArgsLength)
            {
                ArgsLogMode = (LogMode)Enum.Parse(typeof(LogMode), args[MinArgsLength + 0]);
            }

            return true;
        }
    }
}
