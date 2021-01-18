using Statiq.App;
using Statiq.Web;

await Bootstrapper.Factory
    .CreateWeb(args)
    .RunAsync();
