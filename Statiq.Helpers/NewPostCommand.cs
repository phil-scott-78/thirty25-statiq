using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Statiq.App;
using Statiq.Common;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Command = SimpleExec.Command;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Thirty25.Statiq.Helpers
{
    public class NewPostSettings : EngineCommandSettings
    {
        [CommandArgument(0, "[POST_TITLE]")] public string Title { get; set; }

        [CommandOption("-t|--tags <Tags>")] public string Tags { get; set; } = "";

        [CommandOption("-e")]
        [Description("Launch editor after creation")]
        public bool LaunchEditor { get; set; }

        [CommandOption("-b")]
        [Description("Create new Git branch for post")]
        public bool CreateBranch { get; set; }

        [CommandOption("--desc <Description>")]
        public string Description { get; set; } = "";
    }

    public class FrontMatter
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string Date { get; set; }
        public string[] Tags { get; set; }

        public static FrontMatter FromSettings(NewPostSettings settings, DateTime dateTime) =>
            new FrontMatter()
            {
                Title = settings.Title,
                Description = settings.Description,
                Tags = settings.Tags
                    .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(i => i.Trim())
                    .ToArray(),
                Date = dateTime.ToString("yyyy-M-d")
            };

        private static readonly ISerializer s_serializer = new SerializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .Build();

        public string ToYaml()
        {
            return $@"---
{s_serializer.Serialize(this).Trim()}
---";
        }
    }

    public class NewPost : EngineCommand<NewPostSettings>
    {
        protected override async Task<int> ExecuteEngineAsync(CommandContext commandContext, NewPostSettings settings,
            IEngineManager engineManager)
        {
            var fileSystem = engineManager.Engine.FileSystem;
            var dateTime = DateTime.Now;
            var optimizedTitle = NormalizedPath.OptimizeFileName(settings.Title);
            var rootPath = fileSystem.GetRootPath();

            if (settings.CreateBranch)
            {
                CreateBranch(rootPath, optimizedTitle, engineManager);
            }

            var filePath = rootPath
                .Combine($"input/posts/{dateTime:yyyy/MM/}")
                .GetFilePath($"{optimizedTitle}.md");
            var file = fileSystem.GetFile(filePath);

            var frontMatter = FrontMatter
                .FromSettings(settings, dateTime)
                .ToYaml();

            await file.WriteAllTextAsync(frontMatter);


            if (settings.LaunchEditor)
            {
                var args = $"\"{rootPath.FullPath}\" -g \"{filePath}:0\"";
                await Command.RunAsync(
                    "code",
                    args,
                    windowsName: "cmd",
                    windowsArgs: "/c code " + args,
                    noEcho: true);
            }

            engineManager.Engine.Logger.Log(LogLevel.Information, "Wrote new markdown file at {File}", filePath);
            return 0;
        }

        private static void CreateBranch(NormalizedPath rootPath, string optimizedTitle, IEngineManager engineManager)
        {
            using var repo = new Repository(rootPath.FullPath);

            if (repo.Head.FriendlyName != "main" && repo.Head.FriendlyName != "master")
                throw new Exception($"Only branching from main or master is support");

            if (repo.Branches.Any(i => i.FriendlyName == optimizedTitle))
                throw new Exception($"Branch with name \"{optimizedTitle}\" already exists");

            var branchName = $"posts/{optimizedTitle}";

            repo.CreateBranch(branchName);
            engineManager.Engine.Logger.Log(LogLevel.Information, "Created new git branch {Branch}", branchName);
            Commands.Checkout(repo, repo.Branches[branchName]);
            engineManager.Engine.Logger.Log(LogLevel.Information, "Set current git branch to {Branch}", branchName);
        }

        public NewPost(IConfiguratorCollection configurators, Settings settings, IServiceCollection serviceCollection, IFileSystem fileSystem,
            Bootstrapper bootstrapper) : base(configurators, settings, serviceCollection, fileSystem, bootstrapper)
        {
        }
    }
}
