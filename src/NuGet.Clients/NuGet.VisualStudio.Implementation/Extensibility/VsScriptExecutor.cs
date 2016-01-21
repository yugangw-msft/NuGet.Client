using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using NuGet.PackageManagement.VisualStudio;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Task = System.Threading.Tasks.Task;

namespace NuGet.VisualStudio
{
    [Export(typeof(IVsScriptExecutor))]
    public class VsScriptExecutor : IVsScriptExecutor
    {
        private Configuration.ISettings Settings { get; }
        private IScriptExecutor ScriptExecutor { get; }

        [ImportingConstructor]
        public VsScriptExecutor(Configuration.ISettings settings, IScriptExecutor scriptExecutor)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (scriptExecutor == null)
            {
                throw new ArgumentNullException(nameof(scriptExecutor));
            }
        }

        public Task<bool> ExecuteInitScriptAsync(string packageId, string packageVersion)
        {
            if (string.IsNullOrEmpty(packageId))
            {
                throw new ArgumentNullException(CommonResources.Argument_Cannot_Be_Null_Or_Empty, nameof(packageId));
            }

            if (string.IsNullOrEmpty(packageId))
            {
                throw new ArgumentNullException(
                    CommonResources.Argument_Cannot_Be_Null_Or_Empty,
                    nameof(packageVersion));
            }

            var version = new NuGetVersion(packageVersion);
            var packageIdentity = new PackageIdentity(packageId, version);
            return ScriptExecutor.ExecuteInitScriptAsync(packageIdentity);
        }
    }
}
