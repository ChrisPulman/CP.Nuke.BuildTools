// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json.Nodes;
using Nuke.Common;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
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
        public static async Task<string> GetFileFromUrlAsync(string url)
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
        public static void RestoreProjectWorkload(this Project project) =>
            ProcessTasks.StartShell($"dotnet workload restore --project {project?.Path}").AssertZeroExitCode();

        /// <summary>
        /// Restores the solution workloads.
        /// </summary>
        /// <param name="solution">The solution.</param>
        public static void RestoreSolutionWorkloads(this Nuke.Common.ProjectModel.Solution solution) =>
            ProcessTasks.StartShell($"dotnet workload restore {solution}").AssertZeroExitCode();

        /// <summary>
        /// Gets the packable projects.
        /// </summary>
        /// <param name="solution">The solution.</param>
        /// <returns>A List of Projects.</returns>
        public static List<Project>? GetPackableProjects(this Nuke.Common.ProjectModel.Solution solution) =>
            solution?.AllProjects.Where(x => x.GetProperty<bool>("IsPackable")).ToList();

        /// <summary>
        /// Gets the test projects.
        /// </summary>
        /// <param name="solution">The solution.</param>
        /// <returns>A List of Projects.</returns>
        public static List<Project>? GetTestProjects(this Nuke.Common.ProjectModel.Solution solution) =>
            solution?.AllProjects.Where(x => x.GetProperty<bool>("IsTestProject")).ToList();

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
                        return releaseVer?.Contains("preview") == false && version.Item2[0].Equals(releaseVer.Split('.').Select(int.Parse).ToArray()[0]);
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

            if (!File.Exists("dotnet-install.ps1"))
            {
                ProcessTasks.StartShell("powershell -NoProfile -ExecutionPolicy unrestricted -Command Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile 'dotnet-install.ps1';").AssertZeroExitCode();
            }

            foreach (var version in versionsToInstall.Select(arr => $"{arr[0]}.{arr[1]}.{arr[2].ToString().First().ToString()}xx").ToArray())
            {
                Console.WriteLine($"Installing .NET SDK {version}");
                ProcessTasks.StartShell($"powershell -NoProfile -ExecutionPolicy unrestricted -Command ./dotnet-install.ps1 -Channel '{version}';").AssertZeroExitCode();
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
}
