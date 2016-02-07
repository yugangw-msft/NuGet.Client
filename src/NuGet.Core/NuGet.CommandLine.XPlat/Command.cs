using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Dnx.Runtime.Common.CommandLine;
using NuGet.Commands;
using NuGet.Configuration;
using NuGet.Logging;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol.Core.v3;
using System.Globalization;

namespace NuGet.CommandLine.XPlat
{
    class Command
    {
        // Create a caching source provider with the default settings, the sources will be passed in
        //TODO: need this?
        private static CachingSourceProvider _sourceProvider = new CachingSourceProvider(
            new PackageSourceProvider(
                Settings.LoadDefaultSettings(root: null, configFileName: null, machineWideSettings: null)));

        protected bool IsNonInteractive { get; set; }

        protected static async Task<PushCommandResource> GetPushCommandResource(
             CommandOption source,
             ISettings settings)
        {
            //TODO: understand and remove the comment below.
            // CommandLineSourceRepositoryProvider caches repositories to avoid duplicates
            var packageSourceProvider = new PackageSourceProvider(settings);

            // Take the passed in source
            IEnumerable<PackageSource> packageSources;
            if (!string.IsNullOrEmpty(source.Value()))
            {
                packageSources = new PackageSource[] { new PackageSource(source.Value()) };
            }
            else
            {
                packageSources = packageSourceProvider.LoadPackageSources().Where(src => src.IsEnabled);
            }

            SourceRepository repo = packageSources.Select(src => _sourceProvider.CreateRepository(src))
                .Distinct()
                .ToList().FirstOrDefault();
            if (repo == null)
            {
                throw new InvalidOperationException("We don't have valid repository(TODO use resource strings)");
            }
            else
            {
                return await repo.GetResourceAsync<PushCommandResource>();
            }
        }

        protected bool Confirm(string description)
        {
            if (IsNonInteractive)
            {
                return true;
            }

            var currentColor = ConsoleColor.Gray;
            try
            {
                currentColor = System.Console.ForegroundColor;
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                /*LocalizedResourceManager.GetString("ConsoleConfirmMessage")*/
                System.Console.Write(String.Format(CultureInfo.CurrentCulture, "{0} (y/N)", description));
                var result = System.Console.ReadLine();
                /*LocalizedResourceManager.GetString("ConsoleConfirmMessageAccept")*/
                return result.StartsWith("y", StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                System.Console.ForegroundColor = currentColor;
            }
        }
    }
}
