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
                push.Description = "Push to remote source";

                var source = push.Option(
                    "-s|--source <source>",
                    Strings.Source_Description,
                    CommandOptionType.SingleValue);

                var timeout = push.Option(
                    "-t|--timeout <timeout>",
                    Strings.Push_Timeout_Description,
                    CommandOptionType.SingleValue);

                var argRoot = push.Argument(
                    "[root]",
                    Strings.Push_Package_ApiKey_Description,
                    multipleValues: true);

                push.OnExecute(async () =>
                {
                    ISettings setting = Settings.LoadDefaultSettings(Path.GetFullPath("."),
                                configFileName: null,
                                machineWideSettings: null);
                    PushCommandResource pushCommandResource = await GetPushCommandResource(source, setting);

                    string packagePath = string.Empty;
                    if (argRoot.Values.Count > 0)
                    {
                        packagePath = argRoot.Values[0];
                    }

                    string apiKey = string.Empty;
                    if (argRoot.Values.Count > 1)
                    {
                        apiKey = argRoot.Values[1];
                    }

                    int t = 0;
                    int.TryParse(timeout.Value(), out t);
                    await pushCommandResource.Push(packagePath,
                        source.Value(),
                        t,
                        (s) => { return apiKey; },
                        logger);

                    return 0;
                });
            });
        }
    }
}
