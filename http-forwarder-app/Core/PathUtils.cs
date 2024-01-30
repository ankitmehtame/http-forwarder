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

        public static string GetConfFilePath(this IConfiguration configuration, string fileName)
        {
            var pathForConf = configuration.GetConfDirPath();
            var possiblePath = Path.Combine(pathForConf, fileName);
            var filePath = GetFilePath(possiblePath) ?? possiblePath;
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