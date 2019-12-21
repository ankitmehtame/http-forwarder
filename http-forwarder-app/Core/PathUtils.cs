using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace http_forwarder_app.Core
{
    public static class PathUtils
    {
        public static string GetConfFilePath(this IConfiguration configuration, string fileName)
        {
            var appRoot = configuration.GetAppRoot();
            var pathsForConf = new [] { Path.Combine(appRoot, $"conf/{fileName}"), Path.Combine(appRoot, fileName) };
            var filePath = GetFilePath(pathsForConf);
            return filePath ?? pathsForConf.First();
        }

        private static string GetFilePath(string[] possiblePaths)
        {
            return possiblePaths.FirstOrDefault(File.Exists);
        }

        public static string GetAppRoot(this IConfiguration configuration)
        {
            var dir = Path.GetDirectoryName(new Uri(Assembly.GetEntryAssembly().EscapedCodeBase).LocalPath);
            return dir;
        }
    }
}