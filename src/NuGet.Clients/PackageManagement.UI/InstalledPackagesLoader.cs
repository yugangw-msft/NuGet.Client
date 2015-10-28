using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NuGet.ProjectManagement;
using NuGet.Versioning;

namespace NuGet.PackageManagement.UI
{
    public class InstalledPackagesLoader
    {
        IEnumerable<NuGetProject> _projects;

        public InstalledPackagesLoader(IEnumerable<NuGetProject> projects)
        {
            _projects = projects;
        }

        public async Task<InstalledPackages> GetInstalledPackagesAsync(CancellationToken cancellationToken)
        {
            var installedPackages = new InstalledPackages();
            installedPackages.Clear();

            foreach (var project in _projects)
            {
                foreach (var package in (await project.GetInstalledPackagesAsync(cancellationToken)))
                {
                    HashSet<NuGetVersion> versions;
                    if (!installedPackages.TryGetValue(package.PackageIdentity.Id, out versions))
                    {
                        versions = new HashSet<NuGetVersion>();
                        installedPackages.Add(package.PackageIdentity.Id, versions);
                    }

                    versions.Add(package.PackageIdentity.Version);
                }
            }

            return installedPackages;
        }
    }
}
