using System.Collections.Generic;
using System.Linq;
using Statiq.Common;

internal class FullUrlShortCode: SyncShortcode
 {
     public override ShortcodeResult Execute(KeyValuePair<string, string>[] args, string content, IDocument document, IExecutionContext context)
     {
         var env = context.GetString("VERCEL_ENV", "development");
         string host;
         if (env != "production"){
             host = context.GetString("VERCEL_URL", Thirty25.Constants.ProdSiteUrl);
         }
         else
         {
             host = Thirty25.Constants.ProdSiteUrl;
         }

         if (host.EndsWith("/")){
             host = host.Substring(0, host.Length - 1);
         }

         if (host.StartsWith("https://"))
         {
             host = host.Substring(8);
         }

         var path = args.First(x => x.Key == "path").Value ?? "";
         if (path.StartsWith("/"))
         {
             path = path.Substring(1);
         }

         return $"https://{host}/{path}";
     }
  }
