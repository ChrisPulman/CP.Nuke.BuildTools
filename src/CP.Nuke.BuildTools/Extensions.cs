// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net.Http.Headers; // System first
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.StaticFiles;
using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.Git;
using Nuke.Common.Tools.GitHub;
using Nuke.Common.Utilities.Collections;
using Octokit;
using Serilog;
using ProductHeaderValueAlias = Octokit.ProductHeaderValue; // disambiguate

namespace CP.BuildTools;

/// <summary>
/// Extensions.
/// </summary>
public static class Extensions
{
    private static readonly HttpClient HttpClient = CreateDefaultHttpClient();

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
        ProcessTasks.StartShell("dotnet tool update -g dotnet-vs").AssertZeroExitCode();
        ProcessTasks.StartShell("vs where release").AssertZeroExitCode();
        ProcessTasks.StartShell($"vs update release {version}").AssertZeroExitCode();
        ProcessTasks.StartShell($"vs modify release {version} +mobile +desktop +uwp +web").AssertZeroExitCode();
        ProcessTasks.StartShell("vs where release").AssertZeroExitCode();
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

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Restores workloads for a project using dotnet workload restore.
    /// </summary>
    /// <param name="project">The project.</param>
    public static void RestoreProjectWorkload(this Nuke.Common.ProjectModel.Project project) =>
        ProcessTasks.StartShell($"dotnet workload restore --project {project?.Path}").AssertZeroExitCode();

    /// <summary>
    /// Restores workloads for a solution using dotnet workload restore.
    /// </summary>
    /// <param name="solution">The solution.</param>
    public static void RestoreSolutionWorkloads(this Solution solution) =>
        ProcessTasks.StartShell($"dotnet workload restore {solution}").AssertZeroExitCode();

    /// <summary>
    /// Gets projects in the solution that are marked packable.
    /// </summary>
    /// <param name="solution">The solution.</param>
    /// <returns>A list of packable projects or null if solution is null.</returns>
    public static List<Nuke.Common.ProjectModel.Project>? GetPackableProjects(this Solution solution) =>
        solution?.AllProjects.Where(x => x.GetProperty<bool>("IsPackable")).ToList();

    /// <summary>
    /// Gets projects in the solution that are marked as test projects.
    /// </summary>
    /// <param name="solution">The solution.</param>
    /// <returns>A list of test projects or null if solution is null.</returns>
    public static List<Nuke.Common.ProjectModel.Project>? GetTestProjects(this Solution solution) =>
        solution?.AllProjects.Where(x => x.GetProperty<bool>("IsTestProject")).ToList();

    /// <summary>
    /// Gets a project by name.
    /// </summary>
    /// <param name="solution">The solution.</param>
    /// <param name="projectName">Name of the project.</param>
    /// <returns>The matching project or null if not found.</returns>
    public static Nuke.Common.ProjectModel.Project? GetProject(this Solution solution, string projectName) =>
        solution?.Projects.FirstOrDefault(x => x.Name == projectName);

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

        GitTasks.Git($"clone --depth 1 {url} {path.ToString("dn")}");
        GitTasks.Git("checkout", path.ToString("dn"));
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
        const string LatestSdk = "latest-sdk";
        var versionsToInstall = new List<int[]>();
        var lookupVersions = versions.Select(v => (raw: v, parts: v.Split('.').Select(x => x == "x" || !int.TryParse(x, out var i) ? default(int?) : i).ToArray())).ToList();
        try
        {
            var jsonData = await GetFileFromUrlAsync("https://raw.githubusercontent.com/dotnet/core/main/release-notes/releases-index.json").ConfigureAwait(false);
            var releasesArray = JsonNode.Parse(jsonData)?["releases-index"]?.AsArray();
            if (releasesArray == null)
            {
                throw new Exception("Could not parse releases-index.json");
            }

            foreach (var wanted in lookupVersions)
            {
                var stableCandidates = releasesArray.Where(x =>
                    {
                        var releaseVer = x?[LatestSdk]?.ToString();
                        if (string.IsNullOrWhiteSpace(releaseVer))
                        {
                            return false;
                        }

                        if (releaseVer.Contains("preview", StringComparison.OrdinalIgnoreCase) || releaseVer.Contains("rc", StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }

                        var releaseParts = releaseVer.Split('.').Select(int.Parse).ToArray();
                        return wanted.parts[0].HasValue && releaseParts[0] == wanted.parts[0];
                    })
                    .Select(x => x![LatestSdk]!.ToString())
                    .ToList();
                if (stableCandidates.Count == 0)
                {
                    continue;
                }

                var chosen = stableCandidates.Max();
                var chosenParts = chosen?.Split('.').Select(int.Parse).ToArray();
                if (chosenParts?.Length >= 3)
                {
                    if (wanted.parts[1].HasValue)
                    {
                        chosenParts[1] = wanted.parts[1]!.Value;
                    }

                    if (wanted.parts.Length > 2 && wanted.parts[2].HasValue)
                    {
                        chosenParts[2] = wanted.parts[2]!.Value;
                    }

                    if (!versionsToInstall.Any(v => v[0] == chosenParts[0] && v[1] == chosenParts[1] && v[2] == chosenParts[2]))
                    {
                        versionsToInstall.Add(chosenParts);
                    }
                }
            }

            if (versionsToInstall.Count == 0)
            {
                throw new Exception("No matching SDK versions found to install");
            }
        }
        catch (Exception ex)
        {
            Log.Information("Error installing dotNet SDKs: {Message}", ex.Message);
            throw;
        }

        if (!File.Exists("dotnet-install.ps1"))
        {
            ProcessTasks.StartShell("pwsh -NoProfile -ExecutionPolicy unrestricted -Command Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile 'dotnet-install.ps1';").AssertZeroExitCode();
        }

        foreach (var version in versionsToInstall)
        {
            var channel = version[0] < 5 ? $"{version[0]}.{version[1]}" : $"{version[0]}.{version[1]}.{version[2].ToString().First()}xx";
            Console.WriteLine($"Installing .NET SDK Channel {channel}");
            ProcessTasks.StartShell($"pwsh -NoProfile -ExecutionPolicy unrestricted -Command ./dotnet-install.ps1 -Channel '{channel}';").AssertZeroExitCode();
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Installs the ASP.NET Core runtime for the given channel.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="version">Runtime channel version (&gt;= 6).</param>
    /// <exception cref="System.Exception">Thrown when version &lt; 6.</exception>
    public static void InstallAspNetCore(this NukeBuild build, string version)
    {
        if (!float.TryParse(version, out var v) || v < 6)
        {
            throw new Exception("Version must be >= 6");
        }

        if (!File.Exists("dotnet-install.ps1"))
        {
            ProcessTasks.StartShell("pwsh -NoProfile -ExecutionPolicy unrestricted -Command Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile 'dotnet-install.ps1';").AssertZeroExitCode();
        }

        ProcessTasks.StartShell($"pwsh -NoProfile -ExecutionPolicy unrestricted -Command ./dotnet-install.ps1 -Channel {version} -Runtime aspnetcore;").AssertZeroExitCode();
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
        // Use File.Exists to avoid AbsolutePath.Exists ambiguity/exception when file is absent
        if (File.Exists(path))
        {
            return;
        }

        Log.Information("Saving file to path {Path}", path);
        File.WriteAllBytes(path, file);
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
    /// Sets a workflow output variable (writes line to GITHUB_OUTPUT file).
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="name">Output name.</param>
    /// <param name="value">Output value.</param>
    public static void GitHubSetOutput(this NukeBuild build, string name, string value)
    {
        var outputFile = GetEnv("GITHUB_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputFile))
        {
            return;
        }

        File.AppendAllText(outputFile, $"{name}={value}{Environment.NewLine}");
    }

    /// <summary>
    /// Appends markdown to the GitHub Actions step summary.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="markdown">Markdown content.</param>
    public static void GitHubAppendSummary(this NukeBuild build, string markdown)
    {
        var summaryFile = GetEnv("GITHUB_STEP_SUMMARY");
        if (string.IsNullOrWhiteSpace(summaryFile))
        {
            return;
        }

        File.AppendAllText(summaryFile, markdown + Environment.NewLine);
    }

    /// <summary>
    /// Creates a collapsible log group in GitHub Actions.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="title">Group title.</param>
    /// <param name="action">Action to execute inside group.</param>
    public static void GitHubLogGroup(this NukeBuild build, string title, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Console.WriteLine($"::group::{title}");
        try
        {
            action();
        }
        finally
        {
            Console.WriteLine("::endgroup::");
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
        var location = file == null ? string.Empty : $" file={file}" + (line.HasValue ? $",line={line}" : string.Empty) + (col.HasValue ? $",col={col}" : string.Empty);
        Console.WriteLine($"::error{location}::{message}");
    }

    /// <summary>
    /// Emits a warning annotation to the GitHub Actions log.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="message">Warning message.</param>
    public static void GitHubWarning(this NukeBuild build, string message) => Console.WriteLine($"::warning::{message}");

    /// <summary>
    /// Emits a debug annotation to the GitHub Actions log.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="message">Debug message.</param>
    public static void GitHubDebug(this NukeBuild build, string message) => Console.WriteLine($"::debug::{message}");

    /// <summary>
    /// Generates release notes from recent commits.
    /// </summary>
    /// <param name="repo">The repository.</param>
    /// <param name="maxCommits">Maximum commits to include.</param>
    /// <returns>Markdown formatted release notes.</returns>
    public static string GenerateReleaseNotes(this GitRepository repo, int maxCommits = 50)
    {
        ArgumentNullException.ThrowIfNull(repo);

        var commits = GitTasks.Git($"log -n {maxCommits} --pretty=format:%H:::%s").Select(l => l.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("### Commits");
        foreach (var commit in commits)
        {
            var parts = commit.Split(":::", 2);
            if (parts.Length == 2)
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
        var notes = repo.GenerateReleaseNotes(maxCommits);
        return release.UpdateReleaseBody(repo, (release?.Body ?? string.Empty) + System.Environment.NewLine + notes);
    }

    internal static void UploadReleaseAssetToGithub(this Release release, AbsolutePath asset)
    {
        if (!asset.Exists())
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

        var releaseAssetUpload = new ReleaseAssetUpload { ContentType = assetContentType, FileName = Path.GetFileName(asset), RawData = File.OpenRead(asset) };
        _ = GitHubTasks.GitHubClient.Repository.Release.UploadAsset(release, releaseAssetUpload).Result;
        Log.Information("Done Uploading {FileName} to the release", Path.GetFileName(asset));
    }

    internal static Release UploadDirectory(this Release release, AbsolutePath directory)
    {
        if (directory.GlobDirectories("*").Count > 0)
        {
            Log.Warning("Only files on the root of {Directory} directory will be uploaded as release assets", directory);
        }

        directory.GlobFiles("*").ForEach(release.UploadReleaseAssetToGithub);
        return release;
    }

    internal static Release CreateRelease(this NukeBuild build, GitRepository repo, string tagName, string? version, string? commitSha, bool isPrerelease)
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

    internal static Release UpdateReleaseBody(this Release release, GitRepository repo, string body)
    {
        ArgumentNullException.ThrowIfNull(release);

        ArgumentNullException.ThrowIfNull(repo);

        var repoInfo = repo.Identifier.Split('/');
        return GitHubTasks.GitHubClient.Repository.Release.Edit(repoInfo[0], repoInfo[1], release.Id, new ReleaseUpdate { Body = body, Draft = release.Draft, Name = release.Name, Prerelease = release.Prerelease }).Result;
    }

    internal static Release Publish(this Release release, GitRepository repo)
    {
        ArgumentNullException.ThrowIfNull(release);

        ArgumentNullException.ThrowIfNull(repo);

        var repoInfo = repo.Identifier.Split('/');
        return GitHubTasks.GitHubClient.Repository.Release.Edit(repoInfo[0], repoInfo[1], release.Id, new ReleaseUpdate { Draft = false }).Result;
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CP.Nuke.BuildTools", "1.0"));
        return client;
    }

    private static string? GetEnv(string name, string? @default = null) => Environment.GetEnvironmentVariable(name) ?? @default;
}
