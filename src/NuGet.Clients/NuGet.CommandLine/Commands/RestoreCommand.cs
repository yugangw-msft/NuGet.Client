﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Commands;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Logging;
using NuGet.DependencyResolver;
using NuGet.PackageManagement;
using NuGet.Packaging;
using NuGet.ProjectManagement;
using NuGet.ProjectModel;
using NuGet.Protocol.Core.Types;
using NuGet.Repositories;

namespace NuGet.CommandLine
{
    [Command(typeof(NuGetCommand), "restore", "RestoreCommandDescription",
        MinArgs = 0, MaxArgs = 1, UsageSummaryResourceName = "RestoreCommandUsageSummary",
        UsageDescriptionResourceName = "RestoreCommandUsageDescription",
        UsageExampleResourceName = "RestoreCommandUsageExamples")]
    public class RestoreCommand : DownloadCommandBase
    {
        [Option(typeof(NuGetCommand), "RestoreCommandRequireConsent")]
        public bool RequireConsent { get; set; }

        [Option(typeof(NuGetCommand), "RestoreCommandP2PTimeOut")]
        public int Project2ProjectTimeOut { get; set; }

        [Option(typeof(NuGetCommand), "RestoreCommandPackagesDirectory", AltName = "OutputDirectory")]
        public string PackagesDirectory { get; set; }

        [Option(typeof(NuGetCommand), "RestoreCommandSolutionDirectory")]
        public string SolutionDirectory { get; set; }

        [Option(typeof(NuGetCommand), "CommandMSBuildVersion")]
        public string MSBuildVersion { get; set; }

        private static readonly int MaxDegreesOfConcurrency = Environment.ProcessorCount;

        [ImportingConstructor]
        public RestoreCommand()
            : base(MachineCache.Default)
        {
        }

        // The directory that contains msbuild
        private string _msbuildDirectory;

        public override async Task ExecuteCommandAsync()
        {
            CalculateEffectivePackageSaveMode();

            var restoreSummaries = new List<RestoreSummary>();

            _msbuildDirectory = MsBuildUtility.GetMsbuildDirectory(MSBuildVersion, Console);

            if (!string.IsNullOrEmpty(PackagesDirectory))
            {
                PackagesDirectory = Path.GetFullPath(PackagesDirectory);
            }

            if (!string.IsNullOrEmpty(SolutionDirectory))
            {
                SolutionDirectory = Path.GetFullPath(SolutionDirectory);
            }

            var restoreInputs = DetermineRestoreInputs();
            if (restoreInputs.PackageReferenceFiles.Count > 0)
            {
                var v2RestoreResult = await PerformNuGetV2RestoreAsync(restoreInputs);
                restoreSummaries.Add(v2RestoreResult);
            }

            if (restoreInputs.V3RestoreFiles.Count > 0)
            {
                // Read the settings outside of parallel loops.
                ReadSettings(restoreInputs);

                // Convert package sources to repositories
                var sourceProvider = GetSourceRepositoryProvider();
                var repositories = GetPackageSources(Settings).Select(source => sourceProvider.CreateRepository(source))
                    .ToList();

                // Resolve the packages directory
                var globalPackagesFolder = SettingsUtility.GetGlobalPackagesFolder(Settings);
                var packagesDir = GetEffectiveGlobalPackagesFolder(
                                    PackagesDirectory,
                                    SolutionDirectory,
                                    restoreInputs,
                                    globalPackagesFolder);


                var packageSources = GetPackageSources(Settings);

                var providerCache = new RestoreCommandProvidersCache();

                using (var cacheContext = new SourceCacheContext())
                {
                    var isParallel = !DisableParallelProcessing && !RuntimeEnvironmentHelper.IsMono;

                    int maxTasks = isParallel ? MaxDegreesOfConcurrency : 1;

                    if (maxTasks < 1)
                    {
                        maxTasks = 1;
                    }

                    // Throttle the tasks so no more than 16 run at a time, so memory doesn't accumulate.
                    // This should make large project (over 100 project.json files) allocate reasonable
                    // amounts of memory.
                    var tasks = new List<Task<RestoreSummary>>();
                    int restoreCount = restoreInputs.V3RestoreFiles.Count;

                    int currentFileIndex = 0;

                    do
                    {
                        Debug.Assert(tasks.Count < maxTasks);

                        // Fill with max of 16 tasks upfront (or 1 if parallel restore is disabled)
                        // then add one by one as they get completed.
                        int newTasks = maxTasks - tasks.Count;

                        for (int i = 0; currentFileIndex < restoreCount && i < newTasks; i++, currentFileIndex++)
                        {
                            var file = restoreInputs.V3RestoreFiles[currentFileIndex];

                            var collectorLogger = new CollectorLogger(Console);

                            var sharedCache = providerCache.GetOrCreate(
                                packagesDir,
                                repositories,
                                cacheContext,
                                collectorLogger);

                            var newTask = Task.Run(async () => await PerformNuGetV3RestoreAsync(
                                file,
                                restoreInputs.ProjectReferenceLookup,
                                repositories,
                                sharedCache,
                                collectorLogger));

                            tasks.Add(newTask);
                        }

                        var task = await Task.WhenAny(tasks);

                        restoreSummaries.Add(task.Result);
                        tasks.Remove(task);
                    }
                    while (tasks.Count > 0 || currentFileIndex < restoreCount);
                }
            }

            RestoreSummary.Log(Console, restoreSummaries, Verbosity != Verbosity.Quiet);

            if (restoreSummaries.Any(x => !x.Success))
            {
                throw new ExitCodeException(exitCode: 1);
            }
        }

        private static string GetEffectiveGlobalPackagesFolder(
            string packagesDirectoryParameter,
            string solutionDirectoryParameter,
            PackageRestoreInputs packageRestoreInputs,
            string globalPackagesFolder)
        {
            // Return the -PackagesDirectory parameter if specified
            if (!string.IsNullOrEmpty(packagesDirectoryParameter))
            {
                return packagesDirectoryParameter;
            }

            // Return the globalPackagesFolder as-is if it is a full path
            if (Path.IsPathRooted(globalPackagesFolder))
            {
                return globalPackagesFolder;
            }
            else if (!string.IsNullOrEmpty(solutionDirectoryParameter)
                || packageRestoreInputs.RestoringWithSolutionFile)
            {
                var solutionDirectory = packageRestoreInputs.RestoringWithSolutionFile ?
                    packageRestoreInputs.DirectoryOfSolutionFile :
                    solutionDirectoryParameter;

                // -PackagesDirectory parameter was not provided and globalPackagesFolder is a relative path.
                // Use the solutionDirectory to construct the full path
                return Path.Combine(solutionDirectory, globalPackagesFolder);
            }

            // -PackagesDirectory parameter was not provided and globalPackagesFolder is a relative path.
            // solution directory is not available either. Throw
            var message = string.Format(
                CultureInfo.CurrentCulture,
                LocalizedResourceManager.GetString("RestoreCommandCannotDetermineGlobalPackagesFolder"));

            throw new CommandLineException(message);
        }

        private async Task<RestoreSummary> PerformNuGetV3RestoreAsync(
            string inputPath,
            ProjectReferenceCache projectReferences,
            List<SourceRepository> repositories,
            RestoreCommandProviders sharedCache,
            CollectorLogger logger)
        {
            var inputFileName = Path.GetFileName(inputPath);
            var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(inputPath));
            PackageSpec packageSpec = null;
            string projectJsonPath = null;
            string projectName = null;

            // Determine the type of the input and restore it appropriately
            // Inputs can be: project.json files or msbuild project files

            if (BuildIntegratedProjectUtility.IsProjectConfig(inputPath))
            {
                // Restore a project.json file using the directory as the Id
                logger.LogVerbose($"Reading project file {Arguments[0]}");

                projectJsonPath = inputPath;
                projectName = Path.GetFileName(projectDirectory);
            }
            else if (ProjectHelper.UnsupportedProjectExtensions.Contains(Path.GetExtension(inputPath)))
            {
                // Unsupported projects such as DNX's .xproj are a noop and should
                // be treated as a success.
                return new RestoreSummary(true);
            }
            else
            {
                projectName = Path.GetFileNameWithoutExtension(inputPath);
                projectJsonPath = BuildIntegratedProjectUtility.GetProjectConfigPath(projectDirectory, projectName);
            }

            if (projectJsonPath == null || !File.Exists(projectJsonPath))
            {
                return new RestoreSummary(false);
            }

            logger.LogVerbose($"Reading project file {inputPath}");

            packageSpec = projectReferences.GetProjectJson(projectJsonPath);

            if (packageSpec == null)
            {
                packageSpec = JsonPackageSpecReader.GetPackageSpec(
                    projectName,
                    projectJsonPath);
            }

            logger.LogVerbose($"Loaded project {packageSpec.Name} from {packageSpec.FilePath}");

            // Resolve the root directory
            var rootDirectory = PackageSpecResolver.ResolveRootDirectory(inputPath);
            logger.LogVerbose($"Found project root directory: {rootDirectory}");

            logger.LogVerbose($"Using packages directory: {sharedCache.GlobalPackages.RepositoryRoot}");

            // Create a restore request
            using (var request = new RestoreRequest(
                packageSpec,
                sharedCache,
                logger,
                disposeProviders: false))
            {
                var packageSaveMode = EffectivePackageSaveMode;
                if (packageSaveMode != Packaging.PackageSaveMode.None)
                {
                    request.PackageSaveMode = EffectivePackageSaveMode;
                }

                if (DisableParallelProcessing)
                {
                    request.MaxDegreeOfConcurrency = 1;
                }
                else
                {
                    request.MaxDegreeOfConcurrency = PackageManagementConstants.DefaultMaxDegreeOfParallelism;
                }

                sharedCache.CacheContext.NoCache = NoCache;

                // Read the existing lock file, this is needed to support IsLocked=true
                var lockFilePath = BuildIntegratedProjectUtility.GetLockFilePath(projectJsonPath);
                request.LockFilePath = lockFilePath;
                request.ExistingLockFile = BuildIntegratedRestoreUtility.GetLockFile(lockFilePath, logger);

                // Resolve the packages directory
                logger.LogVerbose($"Using packages directory: {request.PackagesDirectory}");

                // Find all external references
                var externalProjectReferences = projectReferences.GetReferences(inputPath);
                request.ExternalProjects.AddRange(externalProjectReferences);

                // Determine which lock file version to use
                request.LockFileVersion = LockFileFormat.Version;

                // MSBuild for VS2015U1 fails when projects are in the lock file since it treats them as packages.
                // To work around that NuGet will downgrade the lock file if there are only csproj references.
                // Projects with zero project references can go to v2, and projects with xproj references must be
                // at least v2 to work.
                if (externalProjectReferences.Any(reference => reference.ExternalProjectReferences.Count > 0)
                    && inputFileName?.EndsWith(XProjUtility.XProjExtension) == false
                    && !externalProjectReferences.Any(child =>
                    child.MSBuildProjectPath?.EndsWith(XProjUtility.XProjExtension) == true))
                {
                    // Fallback to v1 for non-xprojs with p2ps
                    request.LockFileVersion = 1;
                }

                // Check if we can restore based on the nuget.config settings
                CheckRequireConsent();

                // Run the restore
                var command = new NuGet.Commands.RestoreCommand(request);
                var result = await command.ExecuteAsync();
                result.Commit(logger);

                return new RestoreSummary(result, inputPath, Settings, repositories, logger.Errors);
            }
        }

        private CommandLineSourceRepositoryProvider GetSourceRepositoryProvider()
        {
            return new CommandLineSourceRepositoryProvider(SourceProvider);
        }

        private void ReadSettings(PackageRestoreInputs packageRestoreInputs)
        {
            if (!string.IsNullOrEmpty(SolutionDirectory) || packageRestoreInputs.RestoringWithSolutionFile)
            {
                var solutionDirectory = packageRestoreInputs.RestoringWithSolutionFile ?
                    packageRestoreInputs.DirectoryOfSolutionFile :
                    SolutionDirectory;

                // Read the solution-level settings
                var solutionSettingsFile = Path.Combine(
                    solutionDirectory,
                    NuGetConstants.NuGetSolutionSettingsFolder);
                if (ConfigFile != null)
                {
                    ConfigFile = Path.GetFullPath(ConfigFile);
                }

                Settings = Configuration.Settings.LoadDefaultSettings(
                    solutionSettingsFile,
                    configFileName: ConfigFile,
                    machineWideSettings: MachineWideSettings);

                // Recreate the source provider and credential provider
                SourceProvider = PackageSourceBuilder.CreateSourceProvider(Settings);
                SetDefaultCredentialProvider();
            }
        }

        private async Task<RestoreSummary> PerformNuGetV2RestoreAsync(PackageRestoreInputs packageRestoreInputs)
        {
            ReadSettings(packageRestoreInputs);
            var packagesFolderPath = GetPackagesFolder(packageRestoreInputs);

            var sourceRepositoryProvider = new CommandLineSourceRepositoryProvider(SourceProvider);
            var nuGetPackageManager = new NuGetPackageManager(sourceRepositoryProvider, Settings, packagesFolderPath);

            var installedPackageReferences = new HashSet<Packaging.PackageReference>(new PackageReferenceComparer());
            if (packageRestoreInputs.RestoringWithSolutionFile)
            {
                installedPackageReferences.AddRange(packageRestoreInputs
                    .PackageReferenceFiles
                    .SelectMany(file => GetInstalledPackageReferences(file, allowDuplicatePackageIds: true)));
            }
            else if (packageRestoreInputs.PackageReferenceFiles.Count > 0)
            {
                // By default the PackageReferenceFile does not throw
                // if the file does not exist at the specified path.
                // So we'll need to verify that the file exists.
                Debug.Assert(packageRestoreInputs.PackageReferenceFiles.Count == 1,
                    "Only one packages.config file is allowed to be specified " +
                    "at a time when not performing solution restore.");

                var packageReferenceFile = packageRestoreInputs.PackageReferenceFiles[0];
                if (!File.Exists(packageReferenceFile))
                {
                    var message = string.Format(
                        CultureInfo.CurrentCulture,
                        LocalizedResourceManager.GetString("RestoreCommandFileNotFound"),
                        packageReferenceFile);

                    throw new InvalidOperationException(message);
                }

                installedPackageReferences.AddRange(
                    GetInstalledPackageReferences(packageReferenceFile, allowDuplicatePackageIds: true));
            }

            var missingPackageReferences = installedPackageReferences.Where(reference =>
                !nuGetPackageManager.PackageExistsInPackagesFolder(reference.PackageIdentity)).ToArray();

            if (missingPackageReferences.Length == 0)
            {
                var message = string.Format(
                    CultureInfo.CurrentCulture,
                    LocalizedResourceManager.GetString("InstallCommandNothingToInstall"),
                    "packages.config");

                Console.LogMinimal(message);
                return new RestoreSummary(true);
            }

            var packageRestoreData = missingPackageReferences.Select(reference =>
                new PackageRestoreData(
                    reference,
                    new[] { packageRestoreInputs.RestoringWithSolutionFile
                                ? packageRestoreInputs.DirectoryOfSolutionFile
                                : packageRestoreInputs.PackageReferenceFiles[0] },
                    isMissing: true));

            var packageSources = GetPackageSources(Settings);

            var repositories = packageSources
                .Select(sourceRepositoryProvider.CreateRepository)
                .ToArray();

            var installCount = 0;
            var failedEvents = new ConcurrentQueue<PackageRestoreFailedEventArgs>();

            var packageRestoreContext = new PackageRestoreContext(
                nuGetPackageManager,
                packageRestoreData,
                CancellationToken.None,
                packageRestoredEvent: (sender, args) => { Interlocked.Add(ref installCount, args.Restored ? 1 : 0); },
                packageRestoreFailedEvent: (sender, args) => { failedEvents.Enqueue(args); },
                sourceRepositories: repositories,
                maxNumberOfParallelTasks: DisableParallelProcessing
                        ? 1
                        : PackageManagementConstants.DefaultMaxDegreeOfParallelism);

            CheckRequireConsent();

            var collectorLogger = new CollectorLogger(Console);
            var projectContext = new ConsoleProjectContext(collectorLogger)
            {
                PackageExtractionContext = new PackageExtractionContext()
            };

            if (EffectivePackageSaveMode != Packaging.PackageSaveMode.None)
            {
                projectContext.PackageExtractionContext.PackageSaveMode = EffectivePackageSaveMode;
            }

            var result = await PackageRestoreManager.RestoreMissingPackagesAsync(
                packageRestoreContext,
                projectContext);

            return new RestoreSummary(
                result.Restored,
                "packages.config projects",
                Settings.Priority.Select(x => Path.Combine(x.Root, x.FileName)),
                packageSources.Select(x => x.Source),
                installCount,
                collectorLogger.Errors.Concat(failedEvents.Select(e => e.Exception.Message)));
        }

        private void CheckRequireConsent()
        {
            if (RequireConsent)
            {
                var packageRestoreConsent = new PackageRestoreConsent(new SettingsToLegacySettings(Settings));

                if (packageRestoreConsent.IsGranted)
                {
                    var message = string.Format(
                        CultureInfo.CurrentCulture,
                        LocalizedResourceManager.GetString("RestoreCommandPackageRestoreOptOutMessage"),
                        NuGet.Resources.NuGetResources.PackageRestoreConsentCheckBoxText.Replace("&", ""));

                    Console.LogMinimal(message);
                }
                else
                {
                    var message = string.Format(
                        CultureInfo.CurrentCulture,
                        LocalizedResourceManager.GetString("InstallCommandPackageRestoreConsentNotFound"),
                        NuGet.Resources.NuGetResources.PackageRestoreConsentCheckBoxText.Replace("&", ""));

                    throw new CommandLineException(message);
                }
            }
        }

        private PackageRestoreInputs DetermineRestoreInputs()
        {
            var packageRestoreInputs = new PackageRestoreInputs();
            if (Arguments.Count == 0)
            {
                // look for solution files first
                var solutionFileFullPath = GetSolutionFile(Directory.GetCurrentDirectory());
                if (solutionFileFullPath != null)
                {
                    if (Verbosity == Verbosity.Detailed)
                    {
                        Console.WriteLine(
                            LocalizedResourceManager.GetString("RestoreCommandRestoringPackagesForSolution"),
                            solutionFileFullPath);
                    }

                    ProcessSolutionFile(solutionFileFullPath, packageRestoreInputs);
                }
                else if (File.Exists(Constants.PackageReferenceFile)) // look for packages.config file
                {
                    var packagesConfigFileFullPath = Path.GetFullPath(Constants.PackageReferenceFile);
                    if (Verbosity == Verbosity.Detailed)
                    {
                        Console.WriteLine(
                            LocalizedResourceManager.GetString(
                                "RestoreCommandRestoringPackagesFromPackagesConfigFile"));
                    }

                    packageRestoreInputs.PackageReferenceFiles.Add(packagesConfigFileFullPath);
                }
                else
                {
                    throw new InvalidOperationException(
                        LocalizedResourceManager.GetString(
                            "Error_NoSolutionFileNorePackagesConfigFile"));
                }
            }
            else
            {
                // An argument was passed in. It might be a solution file or directory,
                // project file, project.json, or packages.config file

                var projectFilePath = Path.GetFullPath(Arguments.First());
                var projectFileName = Path.GetFileName(projectFilePath);

                if (BuildIntegratedProjectUtility.IsProjectConfig(projectFileName))
                {
                    if (File.Exists(projectFilePath))
                    {
                        // project.json or projName.project.json
                        packageRestoreInputs.V3RestoreFiles.Add(projectFilePath);
                    }
                    else
                    {
                        var message = string.Format(
                            CultureInfo.CurrentCulture,
                            LocalizedResourceManager.GetString("RestoreCommandFileNotFound"),
                            projectFilePath);

                        throw new InvalidOperationException(message);
                    }
                }
                else if (string.Equals(projectFileName, Constants.PackageReferenceFile)
                    || (projectFileName.StartsWith("packages.", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        Path.GetExtension(projectFileName),
                        Path.GetExtension(Constants.PackageReferenceFile), StringComparison.OrdinalIgnoreCase)))
                {
                    // restoring from packages.config or packages.projectname.config file
                    packageRestoreInputs.PackageReferenceFiles.Add(projectFilePath);
                }
                else if (MsBuildUtility.IsMsBuildBasedProject(projectFileName))
                {
                    if (!File.Exists(projectFilePath))
                    {
                        var message = string.Format(
                            CultureInfo.CurrentCulture,
                            LocalizedResourceManager.GetString("RestoreCommandFileNotFound"),
                            projectFilePath);

                        throw new InvalidOperationException(message);
                    }

                    // For msbuild files find the project.json or packages.config file,
                    // if neither exist skip it

                    var projectName = Path.GetFileNameWithoutExtension(projectFileName);
                    var dir = Path.GetDirectoryName(projectFilePath);

                    var projectJsonPath = BuildIntegratedProjectUtility.GetProjectConfigPath(dir, projectName);
                    var packagesConfigPath = GetPackageReferenceFile(projectFilePath);

                    // Check for project.json
                    if (File.Exists(projectJsonPath))
                    {
                        packageRestoreInputs.V3RestoreFiles.Add(projectFilePath);
                    }
                    else if (File.Exists(packagesConfigPath))
                    {
                        // Check for packages.config
                        packageRestoreInputs.PackageReferenceFiles.Add(packagesConfigPath);
                    }
                }
                else
                {
                    // Check if it is a solution file
                    var solutionFileFullPath = GetSolutionFile(projectFilePath);
                    if (solutionFileFullPath == null)
                    {
                        throw new InvalidOperationException(
                            LocalizedResourceManager.GetString("Error_CannotLocateSolutionFile"));
                    }

                    ProcessSolutionFile(solutionFileFullPath, packageRestoreInputs);
                }
            }


            var projectsWithPotentialP2PReferences = packageRestoreInputs
                .V3RestoreFiles
                .Where(MsBuildUtility.IsMsBuildBasedProject)
                .ToArray();

            if (projectsWithPotentialP2PReferences.Length > 0)
            {
                int scaleTimeout;

                if (Project2ProjectTimeOut > 0)
                {
                    scaleTimeout = Project2ProjectTimeOut * 1000;
                }
                else
                {
                    scaleTimeout = MsBuildUtility.MsBuildWaitTime *
                        Math.Max(10, projectsWithPotentialP2PReferences.Length / 2) / 10;
                }

                Console.LogVerbose($"MSBuild P2P timeout [ms]: {scaleTimeout}");

                var referencesLookup = MsBuildUtility.GetProjectReferences(
                    _msbuildDirectory,
                    projectsWithPotentialP2PReferences,
                    scaleTimeout);

                packageRestoreInputs.ProjectReferenceLookup = referencesLookup;
            }

            return packageRestoreInputs;
        }

        private static bool IsSolutionOrProjectFile(string fileName)
        {
            if (!String.IsNullOrEmpty(fileName))
            {
                var extension = Path.GetExtension(fileName);
                var lastFourCharacters = string.Empty;
                var length = extension.Length;

                if (length >= 4)
                {
                    lastFourCharacters = extension.Substring(length - 4);
                }

                return (string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(lastFourCharacters, "proj", StringComparison.OrdinalIgnoreCase));
            }
            return false;
        }

        /// <summary>
        /// Gets the solution file, in full path format. If <paramref name="solutionFileOrDirectory"/> is a file,
        /// that file is returned. Otherwise, searches for a *.sln file in
        /// directory <paramref name="solutionFileOrDirectory"/>. If exactly one sln file is found,
        /// that file is returned. If multiple sln files are found, an exception is thrown.
        /// If no sln files are found, returns null.
        /// </summary>
        /// <param name="solutionFileOrDirectory">The solution file or directory to search for solution files.</param>
        /// <returns>The full path of the solution file. Or null if no solution file can be found.</returns>
        private string GetSolutionFile(string solutionFileOrDirectory)
        {
            //Check if the string passed is a file
            //If it is a file, then check if it a solution or project file
            //For other file types and directories it will fall out of these checks and fail later for invalid inputs
            if (File.Exists(solutionFileOrDirectory) && IsSolutionOrProjectFile(solutionFileOrDirectory))
            {
                return Path.GetFullPath(solutionFileOrDirectory);
            }

            // look for solution files
            var slnFiles = Directory.GetFiles(solutionFileOrDirectory, "*.sln");
            if (slnFiles.Length > 1)
            {
                throw new InvalidOperationException(LocalizedResourceManager.GetString("Error_MultipleSolutions"));
            }

            if (slnFiles.Length == 1)
            {
                return Path.GetFullPath(slnFiles[0]);
            }

            return null;
        }

        private string GetPackagesFolder(PackageRestoreInputs packageRestoreInputs)
        {
            if (!string.IsNullOrEmpty(PackagesDirectory))
            {
                return PackagesDirectory;
            }

            var repositoryPath = SettingsUtility.GetRepositoryPath(Settings);
            if (!string.IsNullOrEmpty(repositoryPath))
            {
                return repositoryPath;
            }

            if (!string.IsNullOrEmpty(SolutionDirectory))
            {
                return Path.Combine(SolutionDirectory, CommandLineConstants.PackagesDirectoryName);
            }

            if (packageRestoreInputs.RestoringWithSolutionFile)
            {
                return Path.Combine(
                    packageRestoreInputs.DirectoryOfSolutionFile,
                    CommandLineConstants.PackagesDirectoryName);
            }

            throw new InvalidOperationException(
                LocalizedResourceManager.GetString("RestoreCommandCannotDeterminePackagesFolder"));
        }

        private static string ConstructPackagesConfigFromProjectName(string projectName)
        {
            // we look for packages.<project name>.config file
            // but we don't want any space in the file name, so convert it to underscore.
            return "packages." + projectName.Replace(' ', '_') + ".config";
        }

        // returns the package reference file associated with the project
        private string GetPackageReferenceFile(string projectFile)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectFile);
            var pathWithProjectName = Path.Combine(
                Path.GetDirectoryName(projectFile),
                ConstructPackagesConfigFromProjectName(projectName));
            if (File.Exists(pathWithProjectName))
            {
                return pathWithProjectName;
            }

            return Path.Combine(
                Path.GetDirectoryName(projectFile),
                Constants.PackageReferenceFile);
        }

        private void ProcessSolutionFile(string solutionFileFullPath, PackageRestoreInputs restoreInputs)
        {
            restoreInputs.DirectoryOfSolutionFile = Path.GetDirectoryName(solutionFileFullPath);

            // restore packages for the solution
            var solutionLevelPackagesConfig = Path.Combine(
                restoreInputs.DirectoryOfSolutionFile,
                NuGetConstants.NuGetSolutionSettingsFolder,
                Constants.PackageReferenceFile);

            if (File.Exists(solutionLevelPackagesConfig))
            {
                restoreInputs.PackageReferenceFiles.Add(solutionLevelPackagesConfig);
            }

            var projectFiles = MsBuildUtility.GetAllProjectFileNames(solutionFileFullPath, _msbuildDirectory);
            foreach (var projectFile in projectFiles)
            {
                if (!File.Exists(projectFile))
                {
                    continue;
                }

                // packages.config
                var packagesConfigFilePath = GetPackageReferenceFile(projectFile);

                // project.json
                var dir = Path.GetDirectoryName(projectFile);
                var projectName = Path.GetFileNameWithoutExtension(projectFile);
                var projectJsonPath = BuildIntegratedProjectUtility.GetProjectConfigPath(dir, projectName);

                // project.json overrides packages.config
                if (File.Exists(projectJsonPath))
                {
                    restoreInputs.V3RestoreFiles.Add(projectFile);
                }
                else if (File.Exists(packagesConfigFilePath))
                {
                    restoreInputs.PackageReferenceFiles.Add(packagesConfigFilePath);
                }
            }
        }

        private class PackageRestoreInputs
        {
            public PackageRestoreInputs()
            {
                ProjectReferenceLookup = new ProjectReferenceCache(Enumerable.Empty<string>());
            }

            public bool RestoringWithSolutionFile => !string.IsNullOrEmpty(DirectoryOfSolutionFile);

            public string DirectoryOfSolutionFile { get; set; }

            public List<string> PackageReferenceFiles { get; } = new List<string>();

            public List<string> V3RestoreFiles { get; } = new List<string>();

            public ProjectReferenceCache ProjectReferenceLookup { get; set; }
        }
    }
}
