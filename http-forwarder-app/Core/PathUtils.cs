using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace http_forwarder_app.Core
{
    public static class PathUtils
    {
        public static string GetConfDirPath(this IConfiguration configuration)
        {
            var appRoot = configuration.GetAppRoot();
            var pathsForConf = new[] { Path.Combine(appRoot, $"conf"), Path.Combine(appRoot, @".\..\conf") };
            var realPath = pathsForConf.FirstOrDefault(Directory.Exists) ?? pathsForConf.First();
            return realPath;
        }

        public static string GetValidStorageDirPath(this IConfiguration configuration)
        {
            var appRoot = configuration.GetAppRoot();
            var pathsForStorage = new[] { configuration.GetConfiguredStoragePath(), Path.Combine(appRoot, $"storage"), Path.Combine(appRoot, @".\..\storage") }
                .Where(p => !string.IsNullOrEmpty(p))
                .ToArray()!;
            var realPath = pathsForStorage.FirstOrDefault(Directory.Exists) ?? pathsForStorage.First();
            return realPath ?? throw new Exception("No valid storage path found!");
        }

        public static string GetConfFilePath(this IConfiguration configuration, string fileName)
        {
            var pathForConf = configuration.GetConfDirPath();
            var filePath = GetFilePath(Path.Combine(pathForConf, fileName));
            filePath ??= Path.Combine(pathForConf, fileName);
            return filePath;
        }

        public static string GetStorageFilePath(this IConfiguration configuration)
        {
            var pathForConf = configuration.GetValidStorageDirPath();
            var filePath = GetFilePath(Path.Combine(pathForConf, "storage.json"));
            filePath ??= Path.Combine(pathForConf, "storage.json");
            return filePath;
        }

        private static string? GetFilePath(params string[] possiblePaths)
        {
            return possiblePaths.FirstOrDefault(File.Exists);
        }

        public static string GetAppRoot(this IConfiguration configuration)
        {
            var dir = Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().Location).LocalPath)!;
            return dir;
        }
    }
}