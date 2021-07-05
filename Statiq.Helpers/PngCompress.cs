using System;
using System.Collections.Immutable;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ByteSizeLib;
using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Statiq.App;
using Statiq.Common;
using TinyPng;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Thirty25.Statiq.Helpers
{
    public class PngCompressSettings : EngineCommandSettings
    {
        [CommandOption("-c")]
        [Description("Compress all files, not just uncommitted files")]
        public bool AllFiles { get; set; }
    }

    public class PngCompressCommand : EngineCommand<PngCompressSettings>
    {
        public PngCompressCommand(IConfiguratorCollection configurators, Settings settings,
            IServiceCollection serviceCollection, Bootstrapper bootstrapper) : base(configurators, settings,
            serviceCollection, bootstrapper)
        {
        }

        protected override async Task<int> ExecuteEngineAsync(CommandContext commandContext,
            PngCompressSettings commandSettings,
            IEngineManager engineManager)
        {
            var tinyPngKey = Environment.GetEnvironmentVariable("TinyPngKey") ??
                             throw new Exception(
                                 "TinyPng key not found. Expected value for environment variable \"TinyPngKey\"");

            var rootPath = engineManager.Engine.FileSystem.RootPath.FullPath;
            using var repo = new Repository(rootPath);
            var status = repo.RetrieveStatus();

            var modifiedPngs = status
                .Where(_ => Path.HasExtension(".png"))
                .Select(i => new NormalizedPath(Path.Combine(rootPath, i.FilePath)))
                .ToImmutableList();

            var pngCompressor = new TinyPngClient(tinyPngKey);

            var pngs = engineManager.Engine.FileSystem
                .GetInputFiles("**/*.png")
                .Select(i => i.Path)
                .Where(i => commandSettings.AllFiles || modifiedPngs.Contains(i))
                .ToImmutableList();

            var totalPre = 0L;
            var totalPost = 0L;

            var message = commandSettings.AllFiles ? "all files" : "checked out files";
            engineManager.Engine.Logger.Log(LogLevel.Information, "Beginning compression on {FileTypes}", message);

            foreach (var png in pngs)
            {
                var preSize = engineManager.Engine.FileSystem.GetFile(png.FullPath).Length;
                totalPre += preSize;

                await pngCompressor
                    .Compress(png.FullPath)
                    .Download()
                    .SaveImageToDisk(png.FullPath);

                var postSize = engineManager.Engine.FileSystem.GetFile(png.FullPath).Length;
                totalPost += postSize;

                var percentCompressed = 1 - postSize / (decimal)preSize;
                engineManager.Engine.Logger.Log(LogLevel.Information,
                    "Compressed {Path}. Reduced from {PreSize} to {PostSize} ({PercentCompressed:P})",
                    png.Name,
                    ByteSize.FromBytes(preSize).ToString(),
                    ByteSize.FromBytes(postSize).ToString(),
                    percentCompressed);
            }

            engineManager.Engine.Logger.Log(LogLevel.Information,
                "Compression complete. Reduced from {PreSize} to {PostSize}",
                ByteSize.FromBytes(totalPre).ToString(),
                ByteSize.FromBytes(totalPost).ToString());

            return 0;
        }
    }
}
