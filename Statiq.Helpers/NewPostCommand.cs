using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Cli;
using Statiq.App;
using Statiq.Common;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Command = SimpleExec.Command;

namespace Thirty25.Statiq.Helpers
{
    public class NewPostSettings : EngineCommandSettings
    {
        [CommandArgument(0, "[POST_TITLE]")] public string Title { get; set; }

        [CommandOption("-t|--tags <Tags>")] public string Tags { get; set; } = "";

        [CommandOption("-e")]
        [Description("Launch editor after creation")]
        public bool LaunchEditor { get; set; }

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
            var filePath = fileSystem.GetRootPath()
                .Combine($"input/posts/{dateTime:yyyy/MM/}")
                .GetFilePath($"{NormalizedPath.OptimizeFileName(settings.Title)}.md");
            var file = fileSystem.GetFile(filePath);

            var frontMatter = FrontMatter
                .FromSettings(settings, dateTime)
                .ToYaml();

            await file.WriteAllTextAsync(frontMatter);


            if (settings.LaunchEditor)
            {
                var args = $"\"{fileSystem.GetRootPath().FullPath}\" -g \"{filePath}:0\"";
                await Command.RunAsync(
                    "code",
                    args,
                    windowsName: "cmd",
                    windowsArgs: "/c code " + args,
                    noEcho: true);
            }

            engineManager.Engine.Logger.Log(LogLevel.Information, "Wrote new markdown file at {file}", filePath);
            return 0;
        }

        public NewPost(IConfiguratorCollection configurators, Settings settings, IServiceCollection serviceCollection,
            Bootstrapper bootstrapper) : base(configurators, settings, serviceCollection, bootstrapper)
        {
        }
    }
}
