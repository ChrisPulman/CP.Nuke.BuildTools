// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

namespace CP.BuildTools
{
    /// <summary>
    /// Extensions.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Publics the nuget source.
        /// </summary>
        /// <param name="_">The .</param>
        /// <returns>The Nuget 3 API source.</returns>
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
        public static string PublicNuGetSource(this NukeBuild _) => "https://api.nuget.org/v3/index.json";

        /// <summary>
        /// Updates the visual studio.
        /// </summary>
        /// <param name="_">The NukeBuild.</param>
        /// <param name="version">The version.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public static Task UpdateVisualStudio(this NukeBuild _, string version = "Enterprise")
        {
            ProcessTasks.StartShell("dotnet tool update -g dotnet-vs").AssertZeroExitCode();
            ProcessTasks.StartShell("vs where release").AssertZeroExitCode();
            ProcessTasks.StartShell($"vs update release {version}").AssertZeroExitCode();
            ProcessTasks.StartShell($"vs modify release {version} +mobile +desktop +uwp +web").AssertZeroExitCode();
            ProcessTasks.StartShell("vs where release").AssertZeroExitCode();
            return Task.CompletedTask;
        }
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter

        /// <summary>
        /// Gets the file from URL asynchronous.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <returns>A string.</returns>
#pragma warning disable RCS1224 // Make method an extension method.
        public static async Task<string> GetFileFromUrlAsync(string url)
#pragma warning restore RCS1224 // Make method an extension method.
        {
            using (var httpClient = new HttpClient())
            {
                var response = await httpClient.GetAsync(url);
                return await response.Content.ReadAsStringAsync();
            }
        }

        /// <summary>
        /// Restores the project workload.
        /// </summary>
        /// <param name="project">The project.</param>
        public static void RestoreProjectWorkload(this Nuke.Common.ProjectModel.Project project) =>
            ProcessTasks.StartShell($"dotnet workload restore --project {project?.Path}").AssertZeroExitCode();

        /// <summary>
        /// Restores the solution workloads.
        /// </summary>
        /// <param name="solution">The solution.</param>
        public static void RestoreSolutionWorkloads(this Solution solution) =>
            ProcessTasks.StartShell($"dotnet workload restore {solution}").AssertZeroExitCode();

        /// <summary>
        /// Gets the packable projects.
        /// </summary>
        /// <param name="solution">The solution.</param>
        /// <returns>A List of Projects.</returns>
        public static List<Nuke.Common.ProjectModel.Project>? GetPackableProjects(this Solution solution) =>
            solution?.AllProjects.Where(x => x.GetProperty<bool>("IsPackable")).ToList();

        /// <summary>
        /// Gets the test projects.
        /// </summary>
        /// <param name="solution">The solution.</param>
        /// <returns>A List of Projects.</returns>
        public static List<Nuke.Common.ProjectModel.Project>? GetTestProjects(this Solution solution) =>
            solution?.AllProjects.Where(x => x.GetProperty<bool>("IsTestProject")).ToList();
        /// <summary>
        /// Gets the project by Name.
        /// </summary>
        /// <param name="solution">The solution.</param>
        /// <param name="projectName">The name of the project to find.</param>
        /// <returns>The Project.</returns>
        public static Nuke.Common.ProjectModel.Project? GetProject(this Solution solution, string projectName) =>
            solution?.Projects.FirstOrDefault(x => x.Name == projectName);

        /// <summary>
        /// Check out the source at the specified url and transfer it to the path.
        /// </summary>
        /// <param name="path">The output path.</param>
        /// <param name="url">The Url to load the source from.</param>
        public static void Checkout(this AbsolutePath path, string url)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            GitTasks.Git($"clone -s -n {url} {path.ToString("dn")}");
            GitTasks.Git("checkout", path.ToString("dn"));
        }

        /// <summary>
        /// Installs the DotNet SDK.
        /// If the full version is not specified, then the latest version will be installed.
        /// </summary>
        /// <param name="_">The .</param>
        /// <param name="versions">The versions. The version must be in the format of either 6.x.x, or 6.0.x, or 6.0.100.</param>
        /// <exception cref="System.Exception">No matching SDK versions found to install.</exception>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
        public static async Task InstallDotNetSdk(this NukeBuild _, params string[] versions)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
        {
            const string latestsdk = "latest-sdk";
            var versionsToInstall = new List<int[]>();
            var lookupVersions = versions.Select(v => (v, v.Split('.').Select(x => x == "x" || !int.TryParse(x, out var i) ? default(int?) : i).ToArray())).ToList();
            var json_data = string.Empty;

            try
            {
                // attempt to download JSON SDK data as a string
                json_data = await GetFileFromUrlAsync("https://raw.githubusercontent.com/dotnet/core/main/release-notes/releases-index.json");

                var releasesArray = JsonNode.Parse(json_data)?.Root["releases-index"]?.AsArray();

                // Find the closest version to the one we want to install
                foreach (var version in lookupVersions)
                {
                    var closestVersion = releasesArray?.Where(x =>
                    {
                        // check if the version is not a preview version and if the major version matches
                        var releaseVer = x?[latestsdk]?.ToString();
                        return releaseVer?.Contains("preview") == false && releaseVer?.Contains("rc") == false && version.Item2[0].Equals(releaseVer.Split('.').Select(int.Parse).ToArray()[0]);
                    }).OrderBy(x => Math.Abs(x![latestsdk]!.ToString().CompareTo(version.v))).First();
                    var verSplit = (closestVersion?[latestsdk]?.ToString())?.Split('.').Select(int.Parse).ToArray();

                    // check if the version is already in the list
                    if (versionsToInstall.Any(x => x[0] == verSplit?[0] && x[1] == verSplit[1] && x[2] == verSplit[2]))
                    {
                        continue;
                    }

                    // check if the version is higher than the one we want to install
                    if (verSplit?[1] > version.Item2[1])
                    {
                        if (version.Item2[1].HasValue)
                        {
                            verSplit[1] = version.Item2[1]!.Value;
                        }
                    }

                    if (verSplit?[2] > version.Item2[2])
                    {
                        if (version.Item2[2].HasValue)
                        {
                            verSplit[2] = version.Item2[2]!.Value;
                        }
                        else
                        {
                            // TODO: if the minor version is not specified, then we want the latest.T
                            // The output must be a string, must be three digits, and must be padded with xx if not three digits
                        }
                    }

                    if (verSplit != null)
                    {
                        versionsToInstall.Add(verSplit);
                    }
                }

                // if versionsToInstall is empty, then we didn't find any versions to install
                if (versionsToInstall.Count == 0)
                {
                    throw new Exception("No matching SDK versions found to install");
                }
            }
            catch (Exception ex)
            {
                Log.Information("Error installing dotNet Sdk's, Error: {Value}", ex.Message);
                throw;
            }

            ProcessTasks.StartShell("pwsh -NoProfile -ExecutionPolicy unrestricted -Command Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile 'dotnet-install.ps1';").AssertZeroExitCode();

            foreach (var version in versionsToInstall.Select(arr => $"{arr[0]}.{arr[1]}.{arr[2].ToString().First()}xx").ToArray())
            {
                var v = version.Split('.').Take(2).Select(int.Parse).ToArray();
                if (v?[0] < 5)
                {
                    // Handle versions less than .Net 5.0 as only accepting 2 digits
                    var ver = $"{v[0]}.{v[1]}";
                    Console.WriteLine($"Installing .NET SDK {ver}");
                    ProcessTasks.StartShell($"pwsh -NoProfile -ExecutionPolicy unrestricted -Command ./dotnet-install.ps1 -Channel '{ver}';").AssertZeroExitCode();
                    continue;
                }

                Console.WriteLine($"Installing .NET SDK {version}");
                ProcessTasks.StartShell($"pwsh -NoProfile -ExecutionPolicy unrestricted -Command ./dotnet-install.ps1 -Channel '{version}';").AssertZeroExitCode();
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <summary>
        /// Installs the ASP net core.
        /// </summary>
        /// <param name="_">The .</param>
        /// <param name="version">The version.</param>
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
        public static void InstallAspNetCore(this NukeBuild _, string version)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
        {
            if (float.Parse(version) < 6)
            {
                throw new Exception("Version must be greater than or equal to 6");
            }

            if (!File.Exists("dotnet-install.ps1"))
            {
                ProcessTasks.StartShell("pwsh -NoProfile -ExecutionPolicy unrestricted -Command Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile 'dotnet-install.ps1';").AssertZeroExitCode();
            }

            ProcessTasks.StartShell($"pwsh -NoProfile -ExecutionPolicy unrestricted -Command ./dotnet-install.ps1 -Channel {version} -Runtime aspnetcore;").AssertZeroExitCode();
        }

        /// <summary>
        /// Gets the asset.
        /// </summary>
        /// <param name="_">The .</param>
        /// <param name="repoOwner">The repo owner.</param>
        /// <param name="repoName">Name of the repo.</param>
        /// <param name="assetName">Name of the asset.</param>
        /// <param name="uiReleaseTag">The UI release tag.</param>
        /// <returns>A byte[].</returns>
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
        public static byte[] GetAsset(this NukeBuild _, string repoOwner, string repoName, string assetName, string? uiReleaseTag)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
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
        /// Saves the file.
        /// </summary>
        /// <param name="_">The .</param>
        /// <param name="path">The path.</param>
        /// <param name="file">The file.</param>
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
        public static void SaveFile(this NukeBuild _, AbsolutePath path, byte[] file)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
        {
            if (path.Exists())
            {
                return;
            }

            Log.Information("Saving file to path {Path}", path);
            File.WriteAllBytes(path, file);
            Log.Information("File saved to path {Path}", path);
        }

        /// <summary>
        /// Sets the github credentials.
        /// </summary>
        /// <param name="_">The .</param>
        /// <param name="authToken">The authentication token.</param>
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
        public static void SetGithubCredentials(this NukeBuild _, string authToken) =>
        GitHubTasks.GitHubClient = new GitHubClient(new ProductHeaderValue(nameof(NukeBuild)))
        {
            Credentials = new(authToken)
        };
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter

        /// <summary>
        /// Uploads the release asset to github.
        /// </summary>
        /// <param name="release">The release.</param>
        /// <param name="asset">The asset.</param>
        internal static void UploadReleaseAssetToGithub(this Release release, AbsolutePath asset)
        {
            if (!asset.Exists())
            {
                return;
            }

            Log.Information("Started Uploading {FileName} to the release", Path.GetFileName(asset));
            if (!new FileExtensionContentTypeProvider().TryGetContentType(asset, out var assetContentType))
            {
                assetContentType = "application/x-binary";
            }

            var releaseAssetUpload = new ReleaseAssetUpload
            {
                ContentType = assetContentType,
                FileName = Path.GetFileName(asset),
                RawData = File.OpenRead(asset)
            };
            _ = GitHubTasks.GitHubClient.Repository.Release.UploadAsset(release, releaseAssetUpload).Result;
            Log.Information("Done Uploading {FileName} to the release", Path.GetFileName(asset));
        }

        /// <summary>
        /// Uploads the directory.
        /// </summary>
        /// <param name="release">The release.</param>
        /// <param name="directory">The directory.</param>
        /// <returns>A Release.</returns>
        internal static Release UploadDirectory(this Release release, AbsolutePath directory)
        {
            if (directory.GlobDirectories("*").Count > 0)
            {
                Log.Warning("Only files on the root of {Directory} directory will be uploaded as release assets", directory);
            }

            directory.GlobFiles("*").ForEach(release.UploadReleaseAssetToGithub);
            return release;
        }

        /// <summary>
        /// Creates the release.
        /// </summary>
        /// <param name="_">The .</param>
        /// <param name="repo">The repo.</param>
        /// <param name="tagName">Name of the tag.</param>
        /// <param name="version">The version.</param>
        /// <param name="commitSha">The commit sha.</param>
        /// <param name="isPrerelease">if set to <c>true</c> [is prerelease].</param>
        /// <returns>
        /// A Release.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">repo.</exception>
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
        internal static Release CreateRelease(this NukeBuild _, GitRepository repo, string tagName, string? version, string? commitSha, bool isPrerelease)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
        {
            if (repo == null)
            {
                throw new ArgumentNullException(nameof(repo));
            }

            Log.Information("Creating release for tag {TagName}", tagName);
            var newRelease = new NewRelease(tagName)
            {
                TargetCommitish = commitSha,
                Draft = true,
                Name = $"Release version {version}",
                Prerelease = isPrerelease,
                Body = string.Empty
            };
            var repoInfo = repo.Identifier.Split('/');
            return GitHubTasks.GitHubClient.Repository.Release.Create(repoInfo[0], repoInfo[1], newRelease).Result;
        }

        /// <summary>
        /// Publishes the specified repo owner.
        /// </summary>
        /// <param name="release">The release.</param>
        /// <param name="repo">The repo.</param>
        /// <returns>
        /// A Release.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">release.</exception>
        internal static Release Publish(this Release release, GitRepository repo)
        {
            if (release == null)
            {
                throw new ArgumentNullException(nameof(release));
            }

            if (repo == null)
            {
                throw new ArgumentNullException(nameof(repo));
            }

            var repoInfo = repo.Identifier.Split('/');
            return GitHubTasks.GitHubClient.Repository.Release
                .Edit(repoInfo[0], repoInfo[1], release.Id, new ReleaseUpdate { Draft = false }).Result;
        }
    }
}
