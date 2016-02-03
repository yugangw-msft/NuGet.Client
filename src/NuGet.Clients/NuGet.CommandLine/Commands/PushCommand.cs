using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;

namespace NuGet.CommandLine
{
    [Command(typeof(NuGetCommand), "push", "PushCommandDescription;DefaultConfigDescription",
        MinArgs = 1, MaxArgs = 2, UsageDescriptionResourceName = "PushCommandUsageDescription",
        UsageSummaryResourceName = "PushCommandUsageSummary", UsageExampleResourceName = "PushCommandUsageExamples")]
    public class PushCommand : Command
    {
        [Option(typeof(NuGetCommand), "PushCommandSourceDescription", AltName = "src")]
        public string Source { get; set; }

        [Option(typeof(NuGetCommand), "CommandApiKey")]
        public string ApiKey { get; set; }

        [Option(typeof(NuGetCommand), "PushCommandTimeoutDescription")]
        public int Timeout { get; set; }

        [Option(typeof(NuGetCommand), "PushCommandDisableBufferingDescription")]
        public bool DisableBuffering { get; set; }

        public override async Task ExecuteCommandAsync()
        {
            //verify source to surface better errors
            string packagePath = Arguments[0];
            string source = ResolveSource(packagePath, ConfigurationDefaults.Instance.DefaultPushSource);
            PushCommandResource pushCommandResource = await GetPushCommandResource(source);

            var pushEndpoint = pushCommandResource.GetPushEndpoint();
            if (string.IsNullOrEmpty(pushEndpoint))
            {
                var message = string.Format(
                    LocalizedResourceManager.GetString("PushCommand_PushNotSupported"),
                    source);
                Console.LogWarning(message);
                return;
            }

            NuGet.Commands.PushCommandBase push = new NuGet.Commands.PushCommandBase(
                packagePath,
                pushCommandResource,
                GetApiKey(pushEndpoint),
                UserAgent.CreateUserAgentString(CommandLineConstants.UserAgent),
                Timeout,
                Console
              );
            await push.ExecuteCommandAsync();
        }

        private string ResolveSource(string packagePath, string configurationDefaultPushSource = null)
        {
            string source = Source;

            if (String.IsNullOrEmpty(source))
            {
                source = SettingsUtility.GetConfigValue(Settings, "DefaultPushSource");
            }

            if (String.IsNullOrEmpty(source))
            {
                source = configurationDefaultPushSource;
            }

            if (!String.IsNullOrEmpty(source))
            {
                source = SourceProvider.ResolveAndValidateSource(source);
            }
            else
            {
                source = packagePath.EndsWith(PackCommand.SymbolsExtension, StringComparison.OrdinalIgnoreCase)
                    ? NuGetConstants.DefaultSymbolServerUrl
                    : NuGetConstants.DefaultGalleryServerUrl;
            }
            return source;
        }

        
        private async Task<PushCommandResource> GetPushCommandResource(string source)
        {
            var packageSource = new Configuration.PackageSource(source);
            var sourceRepository = new CommandLineSourceRepositoryProvider(SourceProvider)
                                       .CreateRepository(packageSource);
            return await sourceRepository.GetResourceAsync<PushCommandResource>();
        }

        private string GetApiKey(string source)
        {
            if (!String.IsNullOrEmpty(ApiKey))
            {
                return ApiKey;
            }

            string apiKey = null;

            // Second argument, if present, should be the API Key
            if (Arguments.Count > 1)
            {
                apiKey = Arguments[1];
            }

            // If the user did not pass an API Key look in the config file
            if (String.IsNullOrEmpty(apiKey))
            {
                apiKey = SettingsUtility.GetDecryptedValue(Settings, "apikeys", source);
            }

            return apiKey;
        }
    }
}