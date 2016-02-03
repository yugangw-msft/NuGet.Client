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

namespace NuGet.CommandLine.XPlat
{
    class PushCommand:Command
    {
        public PushCommand(CommandLineApplication app, ILogger logger)
        {
            app.Command("push", push =>
            {
                //TODO: use resource strings 
                push.Description = "Push to remote source";

                var source = push.Option(
                    "-s|--source <source>",
                    "source",
                    CommandOptionType.SingleValue);

                var apiKey = push.Option(
                    "--apikey <apikey>",
                    "api key",
                    CommandOptionType.SingleValue
                    );

                var timeout = push.Option(
                    "-t|--timeout <timeout>",
                    "timeout in seconds",
                    CommandOptionType.SingleValue);

                var argRoot = push.Argument(
                    "[root]",
                    "package files to push",
                    multipleValues: false);

                push.OnExecute(async () =>
                {
                    ISettings setting = Settings.LoadDefaultSettings(Path.GetFullPath("."),
                                configFileName: null,
                                machineWideSettings: null);
                    PushCommandResource pushCommandResource = await GetPushCommandResource(source, setting);
                    PushCommandBase cmd = new PushCommandBase(argRoot.Value,
                        pushCommandResource,
                        apiKey.Value(),
                        GetUserAgent(),
                        string.IsNullOrEmpty(timeout.Value()) ? 0 : int.Parse(timeout.Value()),
                        logger);
                    await cmd.ExecuteCommandAsync();
                    return 0;
                });

            });
        }
    }
}
