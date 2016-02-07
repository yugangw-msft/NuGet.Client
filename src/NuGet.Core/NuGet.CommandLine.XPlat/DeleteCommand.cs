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
                //TODO: use resource strings and match the nuegt command lines
                delete.Description = "Delete a package";

                var source = delete.Option(
                    "-s|--source <source>",
                    "source",
                    CommandOptionType.SingleValue);

                var apiKey = delete.Option(
                    "--apikey <apikey>",
                    "api key",
                    CommandOptionType.SingleValue
                    );

                var nonInteractive = delete.Option(
                    "--non-interactive <nonInteractive>",
                    "non interactive",
                    CommandOptionType.NoValue);

                var argRoot = delete.Argument(
                    "[root]",
                    "package id and version",
                    multipleValues: true);

                delete.OnExecute(async () =>
                {
                    ISettings setting = Settings.LoadDefaultSettings(Path.GetFullPath("."),
                                configFileName: null,
                                machineWideSettings: null);
                    PushCommandResource pushCommandResource = await GetPushCommandResource(source, setting);

                    var packageId = argRoot.Values[0];
                    //TODO: whataboue delete a package w/o a version
                    var packageVersion = argRoot.Values.Count > 1 ? argRoot.Values[1] : string.Empty;
                    var message = "{0} {1} will be deleted from the {2}. Would you like to continue?";
                    if (Confirm(string.Format(message, packageId, packageVersion, pushCommandResource.PushEndpoint)))
                    {
                        await pushCommandResource.DeletePackage(apiKey.Value(),
                           packageId,
                           packageVersion,
                           logger,
                           CancellationToken.None);
                    }
                    //TODO, extract apikey

                    return 0;
                });
            });
        }
    }
}
