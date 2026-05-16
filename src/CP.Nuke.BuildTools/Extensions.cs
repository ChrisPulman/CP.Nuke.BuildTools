// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.AspNetCore.StaticFiles;
using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.GitHub;
using Nuke.Common.Utilities.Collections;
using Octokit;
using Serilog;
using ProductHeaderValueAlias = Octokit.ProductHeaderValue;

namespace CP.BuildTools;

/// <summary>
/// Extensions for modern NUKE-based .NET builds.
/// </summary>
public static class Extensions
{
    private static readonly string[] SolutionSearchPatterns = ["*.slnx", "*.sln"];

    /// <summary>
    /// Gets the default file patterns that should participate in GitHub Actions build cache keys.
    /// </summary>
    public static IReadOnlyList<string> DefaultGitHubActionsCacheKeyFiles { get; } =
    [
        "**/global.json",
        "**/*.slnx",
        "**/*.sln",
        "**/*.csproj",
        "**/*.fsproj",
        "**/*.vbproj",
        "**/Directory.Build.props",
        "**/Directory.Build.targets",
        "**/Directory.Packages.props",
        "**/Directory.Packages.targets",
        "**/packages.lock.json",
        "**/nuget.config",
    ];

    /// <summary>
    /// Gets the public NuGet v3 source endpoint.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <returns>The NuGet v3 service index URL.</returns>
    public static string PublicNuGetSource(this NukeBuild build) => "https://api.nuget.org/v3/index.json";

    /// <summary>
    /// Updates Visual Studio to the latest release for the specified edition with common workloads.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="version">The edition (Enterprise/Professional/Community).</param>
    /// <returns>A completed task once update commands have been issued.</returns>
    public static Task UpdateVisualStudio(this NukeBuild build, string version = "Enterprise")
    {
        ArgumentNullException.ThrowIfNull(version);

        BuildToolsRuntime.RunShellCommand("dotnet tool update -g dotnet-vs");
        BuildToolsRuntime.RunShellCommand("vs where release");
        BuildToolsRuntime.RunShellCommand($"vs update release {QuoteArgument(version)}");
        BuildToolsRuntime.RunShellCommand($"vs modify release {QuoteArgument(version)} +mobile +desktop +uwp +web");
        BuildToolsRuntime.RunShellCommand("vs where release");
        return Task.CompletedTask;
    }

#pragma warning disable RCS1224
    /// <summary>
    /// Downloads file contents from an HTTP/HTTPS URL.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response body as a string or an empty string if the URL is blank.</returns>
    public static async Task<string> GetFileFromUrlAsync(string url, CancellationToken cancellationToken = default)
#pragma warning restore RCS1224
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        return await BuildToolsRuntime.DownloadStringAsync(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Restores workloads for a project using NUKE's dotnet workload restore wrapper.
    /// </summary>
    /// <param name="project">The project.</param>
    public static void RestoreProjectWorkload(this Nuke.Common.ProjectModel.Project project)
    {
        if (project?.Path == null)
        {
            return;
        }

        BuildToolsRuntime.RunWorkloadRestore(project.Path);
    }

    /// <summary>
    /// Restores workloads for a project path using NUKE's dotnet workload restore wrapper.
    /// </summary>
    /// <param name="projectFile">The project file.</param>
    public static void RestoreProjectWorkload(this AbsolutePath projectFile)
    {
        if (string.IsNullOrWhiteSpace(projectFile))
        {
            return;
        }

        BuildToolsRuntime.RunWorkloadRestore(projectFile);
    }

    /// <summary>
    /// Restores workloads for a solution using NUKE's dotnet workload restore wrapper.
    /// </summary>
    /// <param name="solution">The solution.</param>
    public static void RestoreSolutionWorkloads(this Solution solution)
    {
        if (solution?.Path == null)
        {
            return;
        }

        BuildToolsRuntime.RunWorkloadRestore(solution.Path);
    }

    /// <summary>
    /// Restores workloads for a solution path using NUKE's dotnet workload restore wrapper.
    /// </summary>
    /// <param name="solutionFile">The solution file.</param>
    public static void RestoreSolutionWorkloads(this AbsolutePath solutionFile)
    {
        if (string.IsNullOrWhiteSpace(solutionFile))
        {
            return;
        }

        BuildToolsRuntime.RunWorkloadRestore(solutionFile);
    }

    /// <summary>
    /// Gets projects in the solution that are marked packable.
    /// </summary>
    /// <param name="solution">The solution.</param>
    /// <returns>A list of packable projects.</returns>
    public static List<Nuke.Common.ProjectModel.Project> GetPackableProjects(this Solution? solution) =>
        solution?.AllProjects.Where(IsPackableProject).ToList() ?? [];

    /// <summary>
    /// Gets projects in the solution that are marked as test projects.
    /// </summary>
    /// <param name="solution">The solution.</param>
    /// <returns>A list of test projects.</returns>
    public static List<Nuke.Common.ProjectModel.Project> GetTestProjects(this Solution? solution) =>
        solution?.AllProjects.Where(IsTestProject).ToList() ?? [];

    /// <summary>
    /// Gets projects in the solution that are usually publishable applications.
    /// </summary>
    /// <param name="solution">The solution.</param>
    /// <returns>A list of publishable projects.</returns>
    public static List<Nuke.Common.ProjectModel.Project> GetPublishableProjects(this Solution? solution) =>
        solution?.AllProjects.Where(IsPublishableProject).ToList() ?? [];

    /// <summary>
    /// Gets executable projects in the solution.
    /// </summary>
    /// <param name="solution">The solution.</param>
    /// <returns>A list of executable projects.</returns>
    public static List<Nuke.Common.ProjectModel.Project> GetExecutableProjects(this Solution? solution) =>
        solution?.AllProjects.Where(IsExecutableProject).ToList() ?? [];

    /// <summary>
    /// Gets projects in the solution that target the specified framework.
    /// </summary>
    /// <param name="solution">The solution.</param>
    /// <param name="targetFramework">The target framework moniker.</param>
    /// <returns>A list of matching projects.</returns>
    public static List<Nuke.Common.ProjectModel.Project> GetProjectsTargeting(this Solution? solution, string targetFramework)
    {
        if (solution == null || string.IsNullOrWhiteSpace(targetFramework))
        {
            return [];
        }

        return solution.AllProjects.Where(project => project.GetTargetFrameworks().Contains(targetFramework, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Gets a project by name.
    /// </summary>
    /// <param name="solution">The solution.</param>
    /// <param name="projectName">Name of the project.</param>
    /// <returns>The matching project or null if not found.</returns>
    public static Nuke.Common.ProjectModel.Project? GetProject(this Solution? solution, string projectName) =>
        string.IsNullOrWhiteSpace(projectName) ? null : solution?.AllProjects.FirstOrDefault(x => string.Equals(x.Name, projectName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the target frameworks declared by a NUKE project.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <returns>The target framework monikers.</returns>
    public static IReadOnlyList<string> GetTargetFrameworks(this Nuke.Common.ProjectModel.Project? project)
    {
        if (project == null)
        {
            return [];
        }

        var targetFrameworks = GetProjectProperty(project, "TargetFrameworks");
        if (!string.IsNullOrWhiteSpace(targetFrameworks))
        {
            return SplitPropertyList(targetFrameworks);
        }

        var targetFramework = GetProjectProperty(project, "TargetFramework");
        return string.IsNullOrWhiteSpace(targetFramework) ? [] : [targetFramework];
    }

    /// <summary>
    /// Returns whether a NUKE project is marked packable.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <returns>True when the project is packable.</returns>
    public static bool IsPackableProject(this Nuke.Common.ProjectModel.Project? project) => GetBooleanProjectProperty(project, "IsPackable");

    /// <summary>
    /// Returns whether a NUKE project is marked as a test project.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <returns>True when the project is a test project.</returns>
    public static bool IsTestProject(this Nuke.Common.ProjectModel.Project? project) => GetBooleanProjectProperty(project, "IsTestProject");

    /// <summary>
    /// Returns whether a NUKE project is usually publishable.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <returns>True when the project looks publishable.</returns>
    public static bool IsPublishableProject(this Nuke.Common.ProjectModel.Project? project) =>
        !IsTestProject(project) && (IsExecutableProject(project) || GetBooleanProjectProperty(project, "EnableSdkContainerSupport"));

    /// <summary>
    /// Returns whether a NUKE project produces an executable output.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <returns>True when the project output type is executable.</returns>
    public static bool IsExecutableProject(this Nuke.Common.ProjectModel.Project? project)
    {
        var outputType = GetProjectProperty(project, "OutputType");
        return string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase) ||
               GetBooleanProjectProperty(project, "UseWPF") ||
               GetBooleanProjectProperty(project, "UseWindowsForms") ||
               GetBooleanProjectProperty(project, "UseMaui") ||
               string.Equals(GetProjectProperty(project, "UsingMicrosoftNETSdkWeb"), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Clones a git repository into the specified path.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="url">The URL.</param>
    public static void Checkout(this AbsolutePath path, string url)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        BuildToolsRuntime.RunGitCommand($"clone --depth 1 {QuoteArgument(url)} {QuoteArgument(path.ToString())}");
        BuildToolsRuntime.RunGitCommand($"checkout {QuoteArgument(path.ToString())}");
    }

    /// <summary>
    /// Finds all solution files under a root directory.
    /// </summary>
    /// <param name="rootDirectory">The root directory.</param>
    /// <param name="recursive">True to search subdirectories.</param>
    /// <returns>The matching solution files, sorted with .slnx before .sln.</returns>
    public static IReadOnlyList<AbsolutePath> DiscoverSolutionFiles(this AbsolutePath rootDirectory, bool recursive = true)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            return [];
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return SolutionSearchPatterns
            .SelectMany(pattern => Directory.EnumerateFiles(rootDirectory, pattern, searchOption))
            .OrderByDescending(path => string.Equals(Path.GetExtension(path), ".slnx", StringComparison.OrdinalIgnoreCase))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => (AbsolutePath)path)
            .ToArray();
    }

    /// <summary>
    /// Resolves a single solution file under a root directory.
    /// </summary>
    /// <param name="rootDirectory">The root directory.</param>
    /// <param name="solutionName">Optional file name, relative path, or name without extension.</param>
    /// <param name="recursive">True to search subdirectories.</param>
    /// <param name="preferSlnx">True to prefer .slnx over .sln when both names match.</param>
    /// <returns>The resolved solution file.</returns>
    /// <exception cref="FileNotFoundException">Thrown when no matching solution file exists.</exception>
    /// <exception cref="InvalidOperationException">Thrown when more than one solution file matches.</exception>
    public static AbsolutePath ResolveSolutionFile(this AbsolutePath rootDirectory, string? solutionName = null, bool recursive = true, bool preferSlnx = true)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Root directory must be specified.", nameof(rootDirectory));
        }

        var candidates = rootDirectory.DiscoverSolutionFiles(recursive);
        if (!string.IsNullOrWhiteSpace(solutionName))
        {
            candidates = FilterSolutions(candidates, rootDirectory, solutionName, preferSlnx);
        }

        if (candidates.Count == 0)
        {
            throw new FileNotFoundException($"No solution file was found under '{rootDirectory}'.", solutionName);
        }

        if (candidates.Count > 1)
        {
            var names = string.Join(", ", candidates.Select(x => x.ToString()));
            throw new InvalidOperationException($"Multiple solution files matched. Specify a solution name. Matches: {names}");
        }

        return candidates[0];
    }

    /// <summary>
    /// Lists project paths from a .sln or .slnx file by delegating to the dotnet CLI.
    /// </summary>
    /// <param name="solutionFile">The solution file.</param>
    /// <returns>The project paths listed by the solution.</returns>
    public static IReadOnlyList<AbsolutePath> ListSolutionProjectPaths(this AbsolutePath solutionFile)
    {
        if (string.IsNullOrWhiteSpace(solutionFile))
        {
            return [];
        }

        var solutionDirectory = Path.GetDirectoryName(solutionFile.ToString()) ?? Environment.CurrentDirectory;
        var output = BuildToolsRuntime.RunShellCommand($"dotnet sln {QuoteArgument(solutionFile.ToString())} list");
        return output
            .Select(line => line.Trim())
            .Where(line => IsProjectPathLine(line))
            .Select(line => Path.GetFullPath(line, solutionDirectory))
            .Select(path => (AbsolutePath)path)
            .ToArray();
    }

    /// <summary>
    /// Reads project metadata from all projects listed by a .sln or .slnx file.
    /// </summary>
    /// <param name="solutionFile">The solution file.</param>
    /// <returns>The project metadata.</returns>
    public static IReadOnlyList<DotNetProjectInfo> ReadSolutionProjectInfos(this AbsolutePath solutionFile) =>
        solutionFile.ListSolutionProjectPaths().Select(ReadProjectInfo).ToArray();

    /// <summary>
    /// Reads build-relevant metadata from a project file.
    /// </summary>
    /// <param name="projectFile">The project file.</param>
    /// <returns>The project metadata.</returns>
    public static DotNetProjectInfo ReadProjectInfo(this AbsolutePath projectFile)
    {
        if (string.IsNullOrWhiteSpace(projectFile))
        {
            throw new ArgumentException("Project file must be specified.", nameof(projectFile));
        }

        var document = XDocument.Load(projectFile);
        var projectElement = document.Root ?? throw new InvalidDataException($"Project file '{projectFile}' has no root element.");
        var projectDirectory = Path.GetDirectoryName(projectFile.ToString()) ?? Environment.CurrentDirectory;
        var properties = ReadProperties(projectElement);
        var sdk = ReadSdk(projectElement);
        var targetFrameworks = ReadTargetFrameworks(properties);
        var runtimeIdentifiers = SplitPropertyList(GetProperty(properties, "RuntimeIdentifiers"))
            .Concat(SplitPropertyList(GetProperty(properties, "RuntimeIdentifier")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var packageReferences = ReadItemIncludes(projectElement, "PackageReference");
        var projectReferences = ReadItemIncludes(projectElement, "ProjectReference")
            .Select(path => Path.GetFullPath(path, projectDirectory))
            .Select(path => (AbsolutePath)path)
            .ToArray();
        var outputType = GetProperty(properties, "OutputType");
        var isPackable = GetBooleanProperty(properties, "IsPackable");
        var useWpf = GetBooleanProperty(properties, "UseWPF");
        var useWindowsForms = GetBooleanProperty(properties, "UseWindowsForms");
        var useMaui = GetBooleanProperty(properties, "UseMaui");
        var isWebProject = sdk.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase);
        var isWorkerProject = sdk.Contains("Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase);
        var isWindowsDesktopProject = useWpf || useWindowsForms;
        var isMauiProject = useMaui || sdk.Contains("Microsoft.NET.Sdk.Maui", StringComparison.OrdinalIgnoreCase);
        var isContainerEnabled = GetBooleanProperty(properties, "EnableSdkContainerSupport") ||
                                 !string.IsNullOrWhiteSpace(GetProperty(properties, "ContainerRepository")) ||
                                 packageReferences.Contains("Microsoft.NET.Build.Containers", StringComparer.OrdinalIgnoreCase);
        var isTestProject = GetBooleanProperty(properties, "IsTestProject") ||
                            packageReferences.Any(IsKnownTestPackage);
        var isExecutable = IsExecutableOutput(outputType) || isWebProject || isWorkerProject || isWindowsDesktopProject || isMauiProject;
        var isPublishable = !isTestProject && (isExecutable || isContainerEnabled);

        return new(
            (AbsolutePath)Path.GetFullPath(projectFile.ToString()),
            Path.GetFileNameWithoutExtension(projectFile),
            sdk,
            targetFrameworks,
            runtimeIdentifiers,
            packageReferences,
            projectReferences,
            outputType,
            isPackable,
            isTestProject,
            isPublishable,
            isExecutable,
            isWebProject,
            isWorkerProject,
            isWindowsDesktopProject,
            isMauiProject,
            isContainerEnabled);
    }

    /// <summary>
    /// Reads build-relevant metadata from all project files under a root directory.
    /// </summary>
    /// <param name="rootDirectory">The root directory.</param>
    /// <param name="recursive">True to search subdirectories.</param>
    /// <returns>The project metadata.</returns>
    public static IReadOnlyList<DotNetProjectInfo> ReadProjectInfos(this AbsolutePath rootDirectory, bool recursive = true)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            return [];
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(rootDirectory, "*.*proj", searchOption)
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => ReadProjectInfo((AbsolutePath)path))
            .ToArray();
    }

    /// <summary>
    /// Filters project metadata to projects that are marked packable.
    /// </summary>
    /// <param name="projects">The projects.</param>
    /// <returns>The packable projects.</returns>
    public static IReadOnlyList<DotNetProjectInfo> GetPackableProjectInfos(this IEnumerable<DotNetProjectInfo> projects) =>
        projects.Where(project => project.IsPackable).ToArray();

    /// <summary>
    /// Filters project metadata to test projects.
    /// </summary>
    /// <param name="projects">The projects.</param>
    /// <returns>The test projects.</returns>
    public static IReadOnlyList<DotNetProjectInfo> GetTestProjectInfos(this IEnumerable<DotNetProjectInfo> projects) =>
        projects.Where(project => project.IsTestProject).ToArray();

    /// <summary>
    /// Filters project metadata to publishable projects.
    /// </summary>
    /// <param name="projects">The projects.</param>
    /// <returns>The publishable projects.</returns>
    public static IReadOnlyList<DotNetProjectInfo> GetPublishableProjectInfos(this IEnumerable<DotNetProjectInfo> projects) =>
        projects.Where(project => project.IsPublishable).ToArray();

    /// <summary>
    /// Filters project metadata to executable projects.
    /// </summary>
    /// <param name="projects">The projects.</param>
    /// <returns>The executable projects.</returns>
    public static IReadOnlyList<DotNetProjectInfo> GetExecutableProjectInfos(this IEnumerable<DotNetProjectInfo> projects) =>
        projects.Where(project => project.IsExecutable).ToArray();

    /// <summary>
    /// Filters project metadata to projects targeting the specified framework.
    /// </summary>
    /// <param name="projects">The projects.</param>
    /// <param name="targetFramework">The target framework moniker.</param>
    /// <returns>The matching projects.</returns>
    public static IReadOnlyList<DotNetProjectInfo> GetProjectInfosTargeting(this IEnumerable<DotNetProjectInfo> projects, string targetFramework) =>
        string.IsNullOrWhiteSpace(targetFramework)
            ? []
            : projects.Where(project => project.TargetFrameworks.Contains(targetFramework, StringComparer.OrdinalIgnoreCase)).ToArray();

    /// <summary>
    /// Returns whether a project has the specified package reference.
    /// </summary>
    /// <param name="project">The project metadata.</param>
    /// <param name="packageId">The package id.</param>
    /// <returns>True when the package is referenced.</returns>
    public static bool HasPackageReference(this DotNetProjectInfo project, string packageId)
    {
        ArgumentNullException.ThrowIfNull(project);

        return !string.IsNullOrWhiteSpace(packageId) && project.PackageReferences.Contains(packageId, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a GitHub Actions hashFiles expression from cache key file patterns.
    /// </summary>
    /// <param name="patterns">The file patterns.</param>
    /// <returns>A GitHub Actions expression.</returns>
    public static string GitHubActionsHashFilesExpression(params string[] patterns)
    {
        var selectedPatterns = patterns is { Length: > 0 } ? patterns : DefaultGitHubActionsCacheKeyFiles.ToArray();
        var escapedPatterns = selectedPatterns
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => $"'{pattern.Replace("'", "''", StringComparison.Ordinal)}'");
        return "${{ hashFiles(" + string.Join(", ", escapedPatterns) + ") }}";
    }

#pragma warning disable SA1313
    /// <summary>
    /// Installs requested .NET SDK channels, resolving partial version patterns.
    /// </summary>
    /// <param name="_">The build.</param>
    /// <param name="versions">Version requests (e.g. 7.x.x, 8.0.x, 9.0.100).</param>
    /// <returns>A task representing completion of installation process.</returns>
    /// <exception cref="System.Exception">Thrown if metadata cannot be parsed or no matching versions found.</exception>
    public static async Task InstallDotNetSdk(this NukeBuild _, params string[] versions)
    {
        var channelsToInstall = await ResolveDotNetSdkChannelsAsync(versions).ConfigureAwait(false);
        EnsureDotNetInstallScript();

        foreach (var channel in channelsToInstall)
        {
            BuildToolsRuntime.WriteLine($"Installing .NET SDK Channel {channel}");
            BuildToolsRuntime.RunShellCommand($"pwsh -NoProfile -ExecutionPolicy unrestricted -Command ./dotnet-install.ps1 -Channel '{channel}';");
        }
    }
#pragma warning restore SA1313

    /// <summary>
    /// Installs the ASP.NET Core runtime for the given channel.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="version">Runtime channel version (&gt;= 6).</param>
    /// <exception cref="System.Exception">Thrown when version &lt; 6.</exception>
    public static void InstallAspNetCore(this NukeBuild build, string version)
    {
        if (!Version.TryParse(version, out var parsedVersion) || parsedVersion.Major < 6)
        {
            throw new Exception("Version must be >= 6");
        }

        EnsureDotNetInstallScript();
        BuildToolsRuntime.RunShellCommand($"pwsh -NoProfile -ExecutionPolicy unrestricted -Command ./dotnet-install.ps1 -Channel {version} -Runtime aspnetcore;");
    }

    /// <summary>
    /// Downloads a release asset binary from GitHub.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="repoOwner">Repository owner.</param>
    /// <param name="repoName">Repository name.</param>
    /// <param name="assetName">Asset file name.</param>
    /// <param name="uiReleaseTag">Optional release tag (latest used if null).</param>
    /// <returns>Byte array of asset contents.</returns>
    public static byte[] GetAsset(this NukeBuild build, string repoOwner, string repoName, string assetName, string? uiReleaseTag)
    {
        Log.Information("Getting UI asset '{AssetName}' from repo {RepoOwner}/{RepoName}", assetName, repoOwner, repoName);
        var uiRelease = string.IsNullOrWhiteSpace(uiReleaseTag)
            ? GitHubTasks.GitHubClient.Repository.Release.GetLatest(repoOwner, repoName).Result
            : GitHubTasks.GitHubClient.Repository.Release.Get(repoOwner, repoName, uiReleaseTag).Result;
        var uiAsset = uiRelease.Assets.First(x => x.Name == assetName);
        var downloadedAsset = GitHubTasks.GitHubClient.Connection.Get<byte[]>(new Uri(uiAsset.Url), new Dictionary<string, string>(), "application/octet-stream").Result;
        Log.Information("Download Completed for asset {AssetName} of {ReleaseName}", assetName, uiRelease.Name);
        return downloadedAsset.Body;
    }

    /// <summary>
    /// Saves a file to disk if the path does not already exist.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="path">Destination path.</param>
    /// <param name="file">File bytes.</param>
    public static void SaveFile(this NukeBuild build, AbsolutePath path, byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (BuildToolsRuntime.FileExists(path))
        {
            return;
        }

        Log.Information("Saving file to path {Path}", path);
        BuildToolsRuntime.WriteAllBytes(path, file);
        Log.Information("File saved to path {Path}", path);
    }

    /// <summary>
    /// Configures GitHub client credentials for subsequent API operations.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="authToken">Personal access token.</param>
    public static void SetGithubCredentials(this NukeBuild build, string authToken) =>
        GitHubTasks.GitHubClient = new Octokit.GitHubClient(new ProductHeaderValueAlias(nameof(NukeBuild))) { Credentials = new(authToken) };

    /// <summary>
    /// Indicates if current process is executing within GitHub Actions runner.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <returns>True if running under GitHub Actions, else false.</returns>
    public static bool IsGitHubActions(this NukeBuild build) => string.Equals(GetEnv("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the GitHub repository slug (owner/name).
    /// </summary>
    /// <param name="build">The build.</param>
    /// <returns>Repository slug or null.</returns>
    public static string? GitHubRepository(this NukeBuild build) => GetEnv("GITHUB_REPOSITORY");

    /// <summary>
    /// Gets the full Git ref that triggered the workflow.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <returns>Ref string or null.</returns>
    public static string? GitHubRef(this NukeBuild build) => GetEnv("GITHUB_REF");

    /// <summary>
    /// Gets the commit SHA for the workflow event.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <returns>Commit SHA or null.</returns>
    public static string? GitHubSha(this NukeBuild build) => GetEnv("GITHUB_SHA");

    /// <summary>
    /// Gets the GitHub actor (username) that triggered the workflow.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <returns>Actor name or null.</returns>
    public static string? GitHubActor(this NukeBuild build) => GetEnv("GITHUB_ACTOR");

    /// <summary>
    /// Gets the workspace directory path on the runner.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <returns>Workspace path or null.</returns>
    public static string? GitHubWorkspace(this NukeBuild build) => GetEnv("GITHUB_WORKSPACE");

    /// <summary>
    /// Gets the run number for the workflow.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <returns>Run number or null.</returns>
    public static string? GitHubRunNumber(this NukeBuild build) => GetEnv("GITHUB_RUN_NUMBER");

    /// <summary>
    /// Gets the unique run identifier.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <returns>Run id or null.</returns>
    public static string? GitHubRunId(this NukeBuild build) => GetEnv("GITHUB_RUN_ID");

    /// <summary>
    /// Sets a workflow output variable.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="name">Output name.</param>
    /// <param name="value">Output value.</param>
    public static void GitHubSetOutput(this NukeBuild build, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var outputFile = GetEnv("GITHUB_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputFile) || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        BuildToolsRuntime.AppendAllText(outputFile, FormatGitHubOutput(name, value));
    }

    /// <summary>
    /// Appends markdown to the GitHub Actions step summary.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="markdown">Markdown content.</param>
    public static void GitHubAppendSummary(this NukeBuild build, string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var summaryFile = GetEnv("GITHUB_STEP_SUMMARY");
        if (string.IsNullOrWhiteSpace(summaryFile))
        {
            return;
        }

        BuildToolsRuntime.AppendAllText(summaryFile, markdown + Environment.NewLine);
    }

    /// <summary>
    /// Creates a collapsible log group in GitHub Actions.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="title">Group title.</param>
    /// <param name="action">Action to execute inside group.</param>
    public static void GitHubLogGroup(this NukeBuild build, string title, Action action)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(action);

        BuildToolsRuntime.WriteLine($"::group::{EscapeGitHubCommandValue(title)}");
        try
        {
            action();
        }
        finally
        {
            BuildToolsRuntime.WriteLine("::endgroup::");
        }
    }

    /// <summary>
    /// Emits an error annotation to the GitHub Actions log.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="message">Error message.</param>
    /// <param name="file">Optional file path.</param>
    /// <param name="line">Optional line number.</param>
    /// <param name="col">Optional column number.</param>
    public static void GitHubError(this NukeBuild build, string message, string? file = null, int? line = null, int? col = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        var location = file == null
            ? string.Empty
            : $" file={EscapeGitHubCommandProperty(file)}" +
              (line.HasValue ? $",line={line.Value.ToString(CultureInfo.InvariantCulture)}" : string.Empty) +
              (col.HasValue ? $",col={col.Value.ToString(CultureInfo.InvariantCulture)}" : string.Empty);
        BuildToolsRuntime.WriteLine($"::error{location}::{EscapeGitHubCommandValue(message)}");
    }

    /// <summary>
    /// Emits a warning annotation to the GitHub Actions log.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="message">Warning message.</param>
    public static void GitHubWarning(this NukeBuild build, string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        BuildToolsRuntime.WriteLine($"::warning::{EscapeGitHubCommandValue(message)}");
    }

    /// <summary>
    /// Emits a debug annotation to the GitHub Actions log.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="message">Debug message.</param>
    public static void GitHubDebug(this NukeBuild build, string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        BuildToolsRuntime.WriteLine($"::debug::{EscapeGitHubCommandValue(message)}");
    }

    /// <summary>
    /// Generates release notes from recent commits.
    /// </summary>
    /// <param name="repo">The repository.</param>
    /// <param name="maxCommits">Maximum commits to include.</param>
    /// <returns>Markdown formatted release notes.</returns>
    public static string GenerateReleaseNotes(this GitRepository repo, int maxCommits = 50)
    {
        ArgumentNullException.ThrowIfNull(repo);

        var commits = BuildToolsRuntime.RunGitCommand($"log -n {maxCommits.ToString(CultureInfo.InvariantCulture)} --pretty=format:%H:::%s")
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
        var sb = new StringBuilder();
        sb.AppendLine("### Commits");
        foreach (var commit in commits)
        {
            var parts = commit.Split(":::", 2);
            if (parts.Length == 2 && parts[0].Length >= 7)
            {
                sb.AppendLine($"- {parts[1]} ({parts[0][..7]})");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Appends generated release notes to a release body.
    /// </summary>
    /// <param name="release">The release.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="maxCommits">Maximum commits to include.</param>
    /// <returns>The updated release instance.</returns>
    public static Release AppendReleaseNotes(this Release release, GitRepository repo, int maxCommits = 50)
    {
        ArgumentNullException.ThrowIfNull(release);

        var notes = repo.GenerateReleaseNotes(maxCommits);
        return release.UpdateReleaseBody(repo, (release.Body ?? string.Empty) + Environment.NewLine + notes);
    }

    /// <summary>
    /// Uploads or replaces a release asset on GitHub.
    /// </summary>
    /// <param name="release">The release.</param>
    /// <param name="asset">The asset path.</param>
    public static void UploadReleaseAssetToGithub(this Release release, AbsolutePath asset)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (!File.Exists(asset))
        {
            return;
        }

        Log.Information("Started Uploading {FileName} to the release", Path.GetFileName(asset));
        var existing = release.Assets.FirstOrDefault(a => a.Name == Path.GetFileName(asset));
        if (existing != null)
        {
            Log.Information("Deleting existing asset {FileName}", existing.Name);
            var repoInfo = release.Url.Split('/').SkipWhile(p => p != "repos").ToList();
            if (repoInfo.Count >= 6)
            {
                var owner = repoInfo[1];
                var name = repoInfo[2];
                GitHubTasks.GitHubClient.Repository.Release.DeleteAsset(owner, name, existing.Id).Wait();
            }
        }

        if (!new FileExtensionContentTypeProvider().TryGetContentType(asset, out var assetContentType))
        {
            assetContentType = "application/x-binary";
        }

        using var rawData = File.OpenRead(asset);
        var releaseAssetUpload = new ReleaseAssetUpload { ContentType = assetContentType, FileName = Path.GetFileName(asset), RawData = rawData };
        _ = GitHubTasks.GitHubClient.Repository.Release.UploadAsset(release, releaseAssetUpload).Result;
        Log.Information("Done Uploading {FileName} to the release", Path.GetFileName(asset));
    }

    /// <summary>
    /// Uploads all root files in a directory as GitHub release assets.
    /// </summary>
    /// <param name="release">The release.</param>
    /// <param name="directory">The directory.</param>
    /// <returns>The same release instance for fluent target composition.</returns>
    public static Release UploadDirectory(this Release release, AbsolutePath directory)
    {
        if (directory.GlobDirectories("*").Count > 0)
        {
            Log.Warning("Only files on the root of {Directory} directory will be uploaded as release assets", directory);
        }

        directory.GlobFiles("*").ForEach(release.UploadReleaseAssetToGithub);
        return release;
    }

    /// <summary>
    /// Creates a draft GitHub release.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="tagName">The tag name.</param>
    /// <param name="version">The package version.</param>
    /// <param name="commitSha">The target commit SHA.</param>
    /// <param name="isPrerelease">True to create a prerelease.</param>
    /// <returns>The created release.</returns>
    public static Release CreateRelease(this NukeBuild build, GitRepository repo, string tagName, string? version, string? commitSha, bool isPrerelease)
    {
        ArgumentNullException.ThrowIfNull(repo);

        Log.Information("Creating release for tag {TagName}", tagName);
        var newRelease = new NewRelease(tagName)
        {
            TargetCommitish = commitSha,
            Draft = true,
            Name = $"Release version {version}",
            Prerelease = isPrerelease,
            Body = string.Empty,
        };
        var repoInfo = repo.Identifier.Split('/');
        return GitHubTasks.GitHubClient.Repository.Release.Create(repoInfo[0], repoInfo[1], newRelease).Result;
    }

    /// <summary>
    /// Updates the GitHub release body.
    /// </summary>
    /// <param name="release">The release.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="body">The release body.</param>
    /// <returns>The updated release.</returns>
    public static Release UpdateReleaseBody(this Release release, GitRepository repo, string body)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(repo);

        var repoInfo = repo.Identifier.Split('/');
        return GitHubTasks.GitHubClient.Repository.Release.Edit(repoInfo[0], repoInfo[1], release.Id, new ReleaseUpdate { Body = body, Draft = release.Draft, Name = release.Name, Prerelease = release.Prerelease }).Result;
    }

    /// <summary>
    /// Converts a draft GitHub release into a published release.
    /// </summary>
    /// <param name="release">The release.</param>
    /// <param name="repo">The repository.</param>
    /// <returns>The updated release.</returns>
    public static Release Publish(this Release release, GitRepository repo)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(repo);

        var repoInfo = repo.Identifier.Split('/');
        return GitHubTasks.GitHubClient.Repository.Release.Edit(repoInfo[0], repoInfo[1], release.Id, new ReleaseUpdate { Draft = false }).Result;
    }

    internal static async Task<IReadOnlyList<string>> ResolveDotNetSdkChannelsAsync(params string[] versions)
    {
        if (versions == null || versions.Length == 0)
        {
            throw new Exception("At least one SDK version pattern must be specified");
        }

        const string LatestSdk = "latest-sdk";
        var jsonData = await GetFileFromUrlAsync("https://raw.githubusercontent.com/dotnet/core/main/release-notes/releases-index.json").ConfigureAwait(false);
        var releasesArray = JsonNode.Parse(jsonData)?["releases-index"]?.AsArray();
        if (releasesArray == null)
        {
            throw new Exception("Could not parse releases-index.json");
        }

        var latestSdks = releasesArray
            .Select(node => node?[LatestSdk]?.ToString())
            .Where(version => !string.IsNullOrWhiteSpace(version) &&
                              !version!.Contains("preview", StringComparison.OrdinalIgnoreCase) &&
                              !version.Contains("rc", StringComparison.OrdinalIgnoreCase))
            .Select(version => ParseSdkVersion(version!))
            .Where(version => version != null)
            .Select(version => version!.Value)
            .ToArray();

        var channels = new List<string>();
        foreach (var requestedVersion in versions)
        {
            var requested = ParseRequestedSdkVersion(requestedVersion);
            var candidates = latestSdks
                .Where(sdk => (!requested.Major.HasValue || sdk.Major == requested.Major.Value) &&
                              (!requested.Minor.HasValue || sdk.Minor == requested.Minor.Value))
                .OrderByDescending(sdk => sdk.Major)
                .ThenByDescending(sdk => sdk.Minor)
                .ThenByDescending(sdk => sdk.Patch)
                .ToArray();

            if (candidates.Length == 0)
            {
                continue;
            }

            var chosen = candidates[0];
            var major = requested.Major ?? chosen.Major;
            var minor = requested.Minor ?? chosen.Minor;
            var patch = requested.Patch ?? chosen.Patch;
            var channel = major < 5 ? $"{major}.{minor}" : $"{major}.{minor}.{patch.ToString(CultureInfo.InvariantCulture)[0]}xx";
            if (!channels.Contains(channel, StringComparer.OrdinalIgnoreCase))
            {
                channels.Add(channel);
            }
        }

        if (channels.Count == 0)
        {
            throw new Exception("No matching SDK versions found to install");
        }

        return channels;
    }

    internal static string FormatGitHubOutput(string name, string value)
    {
        if (!value.Contains('\n') && !value.Contains('\r'))
        {
            return $"{name}={value}{Environment.NewLine}";
        }

        var delimiter = $"CP_NUKE_BUILDTOOLS_{name}_EOF";
        while (value.Contains(delimiter, StringComparison.Ordinal))
        {
            delimiter += "_";
        }

        return $"{name}<<{delimiter}{Environment.NewLine}{value}{Environment.NewLine}{delimiter}{Environment.NewLine}";
    }

    private static void EnsureDotNetInstallScript()
    {
        if (!BuildToolsRuntime.FileExists("dotnet-install.ps1"))
        {
            BuildToolsRuntime.RunShellCommand("pwsh -NoProfile -ExecutionPolicy unrestricted -Command Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile 'dotnet-install.ps1';");
        }
    }

    private static IReadOnlyList<AbsolutePath> FilterSolutions(IReadOnlyList<AbsolutePath> candidates, AbsolutePath rootDirectory, string solutionName, bool preferSlnx)
    {
        var normalizedName = solutionName.Trim();
        if (Path.IsPathRooted(normalizedName) || normalizedName.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) || normalizedName.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            var fullPath = Path.GetFullPath(normalizedName, rootDirectory);
            candidates = candidates.Where(path => string.Equals(path.ToString(), fullPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        else
        {
            candidates = candidates
                .Where(path => string.Equals(Path.GetFileName(path), normalizedName, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(Path.GetFileNameWithoutExtension(path), normalizedName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (preferSlnx && candidates.Count > 1)
        {
            var slnx = candidates.Where(path => string.Equals(Path.GetExtension(path), ".slnx", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (slnx.Length > 0)
            {
                return slnx;
            }
        }

        return candidates;
    }

    private static bool IsProjectPathLine(string line) =>
        !string.IsNullOrWhiteSpace(line) &&
        !line.StartsWith("Project(s)", StringComparison.OrdinalIgnoreCase) &&
        !line.StartsWith("-", StringComparison.Ordinal) &&
        (line.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
         line.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
         line.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyDictionary<string, string> ReadProperties(XElement projectElement) =>
        projectElement.Descendants()
            .Where(element => element.Parent?.Name.LocalName == "PropertyGroup")
            .GroupBy(element => element.Name.LocalName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value.Trim(), StringComparer.OrdinalIgnoreCase);

    private static string ReadSdk(XElement projectElement)
    {
        var sdk = projectElement.Attribute("Sdk")?.Value;
        if (!string.IsNullOrWhiteSpace(sdk))
        {
            return sdk;
        }

        return string.Join(";", projectElement.Elements().Where(element => element.Name.LocalName == "Sdk").Select(element => element.Attribute("Name")?.Value).Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static IReadOnlyList<string> ReadTargetFrameworks(IReadOnlyDictionary<string, string> properties) =>
        SplitPropertyList(GetProperty(properties, "TargetFrameworks"))
            .Concat(SplitPropertyList(GetProperty(properties, "TargetFramework")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> ReadItemIncludes(XElement projectElement, string itemName) =>
        projectElement.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, itemName, StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string GetProperty(IReadOnlyDictionary<string, string> properties, string name) =>
        properties.TryGetValue(name, out var value) ? value : string.Empty;

    private static bool GetBooleanProperty(IReadOnlyDictionary<string, string> properties, string name) =>
        bool.TryParse(GetProperty(properties, name), out var value) && value;

    private static bool IsExecutableOutput(string? outputType) =>
        string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownTestPackage(string packageId) =>
        packageId.Equals("TUnit", StringComparison.OrdinalIgnoreCase) ||
        packageId.Equals("NUnit", StringComparison.OrdinalIgnoreCase) ||
        packageId.Equals("xunit", StringComparison.OrdinalIgnoreCase) ||
        packageId.Equals("MSTest.TestFramework", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> SplitPropertyList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool GetBooleanProjectProperty(Nuke.Common.ProjectModel.Project? project, string name)
    {
        if (project == null)
        {
            return false;
        }

        try
        {
            return project.GetProperty<bool>(name);
        }
        catch
        {
            var value = GetProjectProperty(project, name);
            return bool.TryParse(value, out var parsed) && parsed;
        }
    }

    private static string GetProjectProperty(Nuke.Common.ProjectModel.Project? project, string name)
    {
        if (project == null)
        {
            return string.Empty;
        }

        try
        {
            return project.GetProperty<string>(name) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static (int Major, int Minor, int Patch)? ParseSdkVersion(string version)
    {
        var parts = version.Split('.');
        if (parts.Length < 3 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var patch))
        {
            return null;
        }

        return (major, minor, patch);
    }

    private static (int? Major, int? Minor, int? Patch) ParseRequestedSdkVersion(string version)
    {
        var parts = version.Split('.');
        return (ParseVersionPart(parts.ElementAtOrDefault(0)), ParseVersionPart(parts.ElementAtOrDefault(1)), ParseVersionPart(parts.ElementAtOrDefault(2)));
    }

    private static int? ParseVersionPart(string? part) =>
        string.IsNullOrWhiteSpace(part) || string.Equals(part, "x", StringComparison.OrdinalIgnoreCase) || !int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? null
            : value;

    private static string EscapeGitHubCommandValue(string value) =>
        value.Replace("%", "%25", StringComparison.Ordinal)
            .Replace("\r", "%0D", StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal);

    private static string EscapeGitHubCommandProperty(string value) =>
        EscapeGitHubCommandValue(value)
            .Replace(":", "%3A", StringComparison.Ordinal)
            .Replace(",", "%2C", StringComparison.Ordinal);

    private static string QuoteArgument(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string? GetEnv(string name, string? @default = null) => BuildToolsRuntime.GetEnvironmentVariable(name) ?? @default;
}
