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
using System.Threading;

namespace NuGet.CommandLine.XPlat
{
    class DeleteCommand : Command
    {
        public DeleteCommand(CommandLineApplication app, ILogger logger)
        {
            app.Command("delete", delete =>
            {
                delete.Description = Strings.Delete_Description;

                var source = delete.Option(
                    "-s|--source <source>",
                    Strings.Source_Description,
                    CommandOptionType.SingleValue);

                //TODO: communicate to Nuget team, this should go to a top level argument, and
                //should not be handled by individual cmdlet
                var nonInteractive = delete.Option(
                    "--non-interactive <nonInteractive>",
                    Strings.NonInteractive_Description,
                    CommandOptionType.NoValue);

                var argRoot = delete.Argument(
                    "[root]",
                    Strings.Delete_PackageIdAndVersion_Description,
                    multipleValues: true);

                delete.OnExecute(async () =>
                {
                    ISettings setting = Settings.LoadDefaultSettings(Path.GetFullPath("."),
                                configFileName: null,
                                machineWideSettings: null);
                    PushCommandResource pushCommandResource = await GetPushCommandResource(source, setting);

                    var packageId = argRoot.Values[0];
                    var packageVersion = argRoot.Values.Count > 1 ? argRoot.Values[1] : string.Empty;
                    var apiKey = argRoot.Values.Count > 2 ? argRoot.Values[2] : string.Empty;
                    await pushCommandResource.Delete(packageId,
                        packageVersion,
                        source.Value(),
                        s => { return apiKey; },
                        (desc) => { return Confirm(nonInteractive.HasValue(), desc); },
                        logger);
                    return 0;
                });
            });
        }
    }
}
