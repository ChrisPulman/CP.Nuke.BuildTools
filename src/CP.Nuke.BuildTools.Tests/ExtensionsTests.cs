// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using CP.BuildTools;
using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.Tools.GitHub;
using Octokit;
using TUnit.Core;
using TAssert = TUnit.Assertions.Assert;

namespace CP.Nuke.BuildTools.Tests;

internal sealed class ExtensionsTests
{
    private const string RuntimeConstraint = "BuildToolsRuntime";

    private sealed class DummyBuild : NukeBuild
    {
    }

    [Test]
    public async Task PublicNuGetSource_ReturnsExpected()
    {
        var build = new DummyBuild();

        await TAssert.That(build.PublicNuGetSource()).IsEqualTo("https://api.nuget.org/v3/index.json");
    }

    [Test]
    [NotInParallel(RuntimeConstraint)]
    public async Task GetFileFromUrlAsync_BlankUrl_ReturnsEmptyString()
    {
        try
        {
            BuildToolsRuntime.DownloadStringAsync = (_, _) => throw new InvalidOperationException("Downloader should not be called.");

            var result = await Extensions.GetFileFromUrlAsync(" ");

            await TAssert.That(result).IsEqualTo(string.Empty);
        }
        finally
        {
            BuildToolsRuntime.Reset();
        }
    }

    [Test]
    [NotInParallel(RuntimeConstraint)]
    public async Task GetFileFromUrlAsync_UsesRuntimeDownloader()
    {
        try
        {
            string? requestedUrl = null;
            BuildToolsRuntime.DownloadStringAsync = (url, _) =>
            {
                requestedUrl = url;
                return Task.FromResult("content");
            };

            var result = await Extensions.GetFileFromUrlAsync("https://example.test/file.txt");

            await TAssert.That(result).IsEqualTo("content");
            await TAssert.That(requestedUrl).IsEqualTo("https://example.test/file.txt");
        }
        finally
        {
            BuildToolsRuntime.Reset();
        }
    }

    [Test]
    [NotInParallel(RuntimeConstraint)]
    public async Task UpdateVisualStudio_IssuesExpectedCommands()
    {
        var commands = new List<string>();

        try
        {
            BuildToolsRuntime.RunShellCommand = command =>
            {
                commands.Add(command);
                return [];
            };

            await new DummyBuild().UpdateVisualStudio("Community");

            await TAssert.That(commands.SequenceEqual(
            [
                "dotnet tool update -g dotnet-vs",
                "vs where release",
                "vs update release \"Community\"",
                "vs modify release \"Community\" +mobile +desktop +uwp +web",
                "vs where release",
            ])).IsTrue();
        }
        finally
        {
            BuildToolsRuntime.Reset();
        }
    }

    [Test]
    [NotInParallel(RuntimeConstraint)]
    public async Task WorkloadRestore_UsesRuntimeHook()
    {
        var restored = new List<string>();

        try
        {
            BuildToolsRuntime.RunWorkloadRestore = restored.Add;

            ((AbsolutePath)"C:\\repo\\app.csproj").RestoreProjectWorkload();
            ((AbsolutePath)"C:\\repo\\app.slnx").RestoreSolutionWorkloads();

            await TAssert.That(restored.SequenceEqual(["C:\\repo\\app.csproj", "C:\\repo\\app.slnx"])).IsTrue();
        }
        finally
        {
            BuildToolsRuntime.Reset();
        }
    }

    [Test]
    public async Task NullSolutionFilters_ReturnEmptyCollections()
    {
        global::Nuke.Common.ProjectModel.Solution? solution = null;

        await TAssert.That(solution.GetPackableProjects().Count).IsEqualTo(0);
        await TAssert.That(solution.GetTestProjects().Count).IsEqualTo(0);
        await TAssert.That(solution.GetPublishableProjects().Count).IsEqualTo(0);
        await TAssert.That(solution.GetExecutableProjects().Count).IsEqualTo(0);
        await TAssert.That(solution.GetProjectsTargeting("net10.0").Count).IsEqualTo(0);
        await TAssert.That(solution.GetProject("Any")).IsNull();
    }

    [Test]
    [NotInParallel(RuntimeConstraint)]
    public async Task Checkout_InvalidArguments_DoesNotInvokeGit()
    {
        var completed = false;

        try
        {
            BuildToolsRuntime.RunGitCommand = _ => throw new InvalidOperationException("Git should not be called.");

            ((AbsolutePath)"C:\\repo").Checkout(string.Empty);
            completed = true;

            await TAssert.That(completed).IsTrue();
        }
        finally
        {
            BuildToolsRuntime.Reset();
        }
    }

    [Test]
    [NotInParallel(RuntimeConstraint)]
    public async Task Checkout_ValidArguments_IssuesCloneAndCheckout()
    {
        var commands = new List<string>();

        try
        {
            BuildToolsRuntime.RunGitCommand = command =>
            {
                commands.Add(command);
                return [];
            };

            ((AbsolutePath)"C:\\repo\\external").Checkout("https://github.com/example/repo.git");

            await TAssert.That(commands.SequenceEqual(
            [
                "clone --depth 1 \"https://github.com/example/repo.git\" \"C:\\repo\\external\"",
                "checkout \"C:\\repo\\external\"",
            ])).IsTrue();
        }
        finally
        {
            BuildToolsRuntime.Reset();
        }
    }

    [Test]
    public async Task DiscoverAndResolveSolutionFiles_PrefersSlnxAndDetectsAmbiguity()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDirectory, "App.sln"), string.Empty);
            File.WriteAllText(Path.Combine(tempDirectory, "App.slnx"), "<Solution />");
            Directory.CreateDirectory(Path.Combine(tempDirectory, "src"));
            File.WriteAllText(Path.Combine(tempDirectory, "src", "Other.slnx"), "<Solution />");

            var discovered = ((AbsolutePath)tempDirectory).DiscoverSolutionFiles();
            var resolved = ((AbsolutePath)tempDirectory).ResolveSolutionFile("App");

            await TAssert.That(discovered.Count).IsEqualTo(3);
            await TAssert.That(Path.GetFileName(resolved)).IsEqualTo("App.slnx");
            await TAssert.That(() => ((AbsolutePath)tempDirectory).ResolveSolutionFile()).Throws<InvalidOperationException>();
            await TAssert.That(() => ((AbsolutePath)tempDirectory).ResolveSolutionFile("Missing")).Throws<FileNotFoundException>();
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Test]
    [NotInParallel(RuntimeConstraint)]
    public async Task ListSolutionProjectPaths_DelegatesToDotNetCliAndNormalizesPaths()
    {
        var tempDirectory = CreateTempDirectory();
        var solution = Path.Combine(tempDirectory, "App.slnx");

        try
        {
            File.WriteAllText(solution, "<Solution />");
            BuildToolsRuntime.RunShellCommand = command =>
            {
                return command.Contains("dotnet sln", StringComparison.Ordinal)
                    ? ["Project(s)", "----------", "src/App/App.csproj", "tests/App.Tests/App.Tests.csproj"]
                    : [];
            };

            var projects = ((AbsolutePath)solution).ListSolutionProjectPaths();

            await TAssert.That(projects.Count).IsEqualTo(2);
            await TAssert.That(projects[0].ToString()).IsEqualTo(Path.Combine(tempDirectory, "src", "App", "App.csproj"));
            await TAssert.That(projects[1].ToString()).IsEqualTo(Path.Combine(tempDirectory, "tests", "App.Tests", "App.Tests.csproj"));
        }
        finally
        {
            BuildToolsRuntime.Reset();
            DeleteDirectory(tempDirectory);
        }
    }

    [Test]
    public async Task ReadProjectInfo_DetectsModernProjectCapabilities()
    {
        var tempDirectory = CreateTempDirectory();
        var projectDirectory = Path.Combine(tempDirectory, "App");
        Directory.CreateDirectory(projectDirectory);
        var projectFile = Path.Combine(projectDirectory, "App.csproj");
        const string ProjectXml = """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFrameworks>net10.0;net10.0-windows10.0.19041.0</TargetFrameworks>
                <RuntimeIdentifiers>win-x64;linux-x64</RuntimeIdentifiers>
                <OutputType>Exe</OutputType>
                <EnableSdkContainerSupport>true</EnableSdkContainerSupport>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="TUnit" Version="1.44.39" />
                <ProjectReference Include="..\Shared\Shared.csproj" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(projectFile, ProjectXml);

        try
        {
            var info = ((AbsolutePath)projectFile).ReadProjectInfo();

            await TAssert.That(info.Name).IsEqualTo("App");
            await TAssert.That(info.IsWebProject).IsTrue();
            await TAssert.That(info.IsExecutable).IsTrue();
            await TAssert.That(info.IsContainerEnabled).IsTrue();
            await TAssert.That(info.IsTestProject).IsTrue();
            await TAssert.That(info.IsPublishable).IsFalse();
            await TAssert.That(info.TargetFrameworks.Contains("net10.0")).IsTrue();
            await TAssert.That(info.RuntimeIdentifiers.Contains("linux-x64")).IsTrue();
            await TAssert.That(info.HasPackageReference("tunit")).IsTrue();
            await TAssert.That(info.ProjectReferences.Count).IsEqualTo(1);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Test]
    public async Task ReadProjectInfos_ReturnsProjectsUnderRoot()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var projectDirectory = Path.Combine(tempDirectory, "src", "Lib");
            Directory.CreateDirectory(projectDirectory);
            var projectFile = Path.Combine(projectDirectory, "Lib.csproj");
            const string ProjectXml = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>
                </Project>
                """;
            File.WriteAllText(projectFile, ProjectXml);

            var projects = ((AbsolutePath)tempDirectory).ReadProjectInfos();

            await TAssert.That(projects.Single().Name).IsEqualTo("Lib");
            await TAssert.That(projects.Single().IsPackable).IsTrue();
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Test]
    public async Task ProjectInfoFilters_ReturnExpectedSubsets()
    {
        var packable = new DotNetProjectInfo((AbsolutePath)"C:\\repo\\Lib.csproj", "Lib", "Microsoft.NET.Sdk", ["net10.0"], [], [], [], null, true, false, false, false, false, false, false, false, false);
        var test = new DotNetProjectInfo((AbsolutePath)"C:\\repo\\Tests.csproj", "Tests", "Microsoft.NET.Sdk", ["net10.0"], [], ["TUnit"], [], "Exe", false, true, false, true, false, false, false, false, false);
        var app = new DotNetProjectInfo((AbsolutePath)"C:\\repo\\App.csproj", "App", "Microsoft.NET.Sdk", ["net10.0"], [], [], [], "Exe", false, false, true, true, false, false, false, false, false);
        var projects = new[] { packable, test, app };

        await TAssert.That(projects.GetPackableProjectInfos().Single().Name).IsEqualTo("Lib");
        await TAssert.That(projects.GetTestProjectInfos().Single().Name).IsEqualTo("Tests");
        await TAssert.That(projects.GetPublishableProjectInfos().Single().Name).IsEqualTo("App");
        await TAssert.That(projects.GetExecutableProjectInfos().Count).IsEqualTo(2);
        await TAssert.That(projects.GetProjectInfosTargeting("net10.0").Count).IsEqualTo(3);
    }

    [Test]
    public async Task GitHubActionsHashFilesExpression_UsesSlnxAwareDefaults()
    {
        var expression = Extensions.GitHubActionsHashFilesExpression();

        await TAssert.That(Extensions.DefaultGitHubActionsCacheKeyFiles.Contains("**/*.slnx")).IsTrue();
        await TAssert.That(expression.Contains("**/*.slnx", StringComparison.Ordinal)).IsTrue();
        await TAssert.That(Extensions.GitHubActionsHashFilesExpression("a", "b")).IsEqualTo("${{ hashFiles('a', 'b') }}");
    }

    [Test]
    [NotInParallel(RuntimeConstraint)]
    public async Task InstallDotNetSdk_ResolvesStableChannelsAndRunsInstallCommands()
    {
        var commands = new List<string>();
        var lines = new List<string>();

        try
        {
            BuildToolsRuntime.DownloadStringAsync = (_, _) => Task.FromResult("""
                {
                  "releases-index": [
                    { "latest-sdk": "10.0.300" },
                    { "latest-sdk": "9.0.203" },
                    { "latest-sdk": "9.0.100-preview.1" }
                  ]
                }
                """);
            BuildToolsRuntime.FileExists = _ => true;
            BuildToolsRuntime.WriteLine = lines.Add;
            BuildToolsRuntime.RunShellCommand = command =>
            {
                commands.Add(command);
                return [];
            };

            await new DummyBuild().InstallDotNetSdk("10.x.x", "9.0.100", "9.0.100");

            await TAssert.That(lines.SequenceEqual(["Installing .NET SDK Channel 10.0.3xx", "Installing .NET SDK Channel 9.0.1xx"])).IsTrue();
            await TAssert.That(commands.SequenceEqual(
            [
                "pwsh -NoProfile -ExecutionPolicy unrestricted -Command ./dotnet-install.ps1 -Channel '10.0.3xx';",
                "pwsh -NoProfile -ExecutionPolicy unrestricted -Command ./dotnet-install.ps1 -Channel '9.0.1xx';",
            ])).IsTrue();
        }
        finally
        {
            BuildToolsRuntime.Reset();
        }
    }

    [Test]
    [NotInParallel(RuntimeConstraint)]
    public async Task InstallDotNetSdk_NoMatchThrows()
    {
        try
        {
            BuildToolsRuntime.DownloadStringAsync = (_, _) => Task.FromResult("""{ "releases-index": [ { "latest-sdk": "10.0.300" } ] }""");

            await TAssert.That(async () => await new DummyBuild().InstallDotNetSdk("7.x.x")).Throws<Exception>();
        }
        finally
        {
            BuildToolsRuntime.Reset();
        }
    }

    [Test]
    [NotInParallel(RuntimeConstraint)]
    public async Task InstallAspNetCore_ValidatesVersionAndRunsRuntimeInstall()
    {
        var commands = new List<string>();

        try
        {
            BuildToolsRuntime.FileExists = _ => true;
            BuildToolsRuntime.RunShellCommand = command =>
            {
                commands.Add(command);
                return [];
            };

            new DummyBuild().InstallAspNetCore("10.0");

            await TAssert.That(commands.Single()).IsEqualTo("pwsh -NoProfile -ExecutionPolicy unrestricted -Command ./dotnet-install.ps1 -Channel 10.0 -Runtime aspnetcore;");
            await TAssert.That(() => new DummyBuild().InstallAspNetCore("5.0")).Throws<Exception>();
        }
        finally
        {
            BuildToolsRuntime.Reset();
        }
    }

    [Test]
    [NotInParallel(RuntimeConstraint)]
    public async Task SaveFile_WritesWhenMissingAndSkipsWhenPresent()
    {
        var written = new List<(string Path, byte[] Data)>();

        try
        {
            BuildToolsRuntime.FileExists = path => path.EndsWith("existing.bin", StringComparison.OrdinalIgnoreCase);
            BuildToolsRuntime.WriteAllBytes = (path, data) => written.Add((path, data));

            var build = new DummyBuild();
            build.SaveFile((AbsolutePath)"C:\\repo\\new.bin", [1, 2, 3]);
            build.SaveFile((AbsolutePath)"C:\\repo\\existing.bin", [4, 5, 6]);

            await TAssert.That(written.Count).IsEqualTo(1);
            await TAssert.That(written[0].Path).IsEqualTo("C:\\repo\\new.bin");
            await TAssert.That(written[0].Data.SequenceEqual(new byte[] { 1, 2, 3 })).IsTrue();
        }
        finally
        {
            BuildToolsRuntime.Reset();
        }
    }

    [Test]
    [NotInParallel(RuntimeConstraint)]
    public async Task GitHubEnvironmentHelpers_ReadRuntimeEnvironment()
    {
        try
        {
            var values = new Dictionary<string, string?>
            {
                ["GITHUB_ACTIONS"] = "true",
                ["GITHUB_REPOSITORY"] = "owner/repo",
                ["GITHUB_REF"] = "refs/heads/main",
                ["GITHUB_SHA"] = "abcdef",
                ["GITHUB_ACTOR"] = "actor",
                ["GITHUB_WORKSPACE"] = "C:\\workspace",
                ["GITHUB_RUN_NUMBER"] = "42",
                ["GITHUB_RUN_ID"] = "100",
            };
            BuildToolsRuntime.GetEnvironmentVariable = name => values.TryGetValue(name, out var value) ? value : null;

            var build = new DummyBuild();

            await TAssert.That(build.IsGitHubActions()).IsTrue();
            await TAssert.That(build.GitHubRepository()).IsEqualTo("owner/repo");
            await TAssert.That(build.GitHubRef()).IsEqualTo("refs/heads/main");
            await TAssert.That(build.GitHubSha()).IsEqualTo("abcdef");
            await TAssert.That(build.GitHubActor()).IsEqualTo("actor");
            await TAssert.That(build.GitHubWorkspace()).IsEqualTo("C:\\workspace");
            await TAssert.That(build.GitHubRunNumber()).IsEqualTo("42");
            await TAssert.That(build.GitHubRunId()).IsEqualTo("100");
        }
        finally
        {
            BuildToolsRuntime.Reset();
        }
    }

    [Test]
    [NotInParallel(RuntimeConstraint)]
    public async Task GitHubOutputAndSummary_WriteExpectedFiles()
    {
        var appended = new List<(string Path, string Content)>();

        try
        {
            BuildToolsRuntime.GetEnvironmentVariable = name => name switch
            {
                "GITHUB_OUTPUT" => "output.txt",
                "GITHUB_STEP_SUMMARY" => "summary.md",
                _ => null,
            };
            BuildToolsRuntime.AppendAllText = (path, content) => appended.Add((path, content));

            var build = new DummyBuild();
            build.GitHubSetOutput("single", "value");
            build.GitHubSetOutput("multi", "line1\nline2");
            build.GitHubAppendSummary("markdown");

            await TAssert.That(appended.Count).IsEqualTo(3);
            await TAssert.That(appended[0]).IsEqualTo(("output.txt", $"single=value{Environment.NewLine}"));
            await TAssert.That(appended[1].Content.Contains("multi<<CP_NUKE_BUILDTOOLS_multi_EOF", StringComparison.Ordinal)).IsTrue();
            await TAssert.That(appended[2]).IsEqualTo(("summary.md", $"markdown{Environment.NewLine}"));
        }
        finally
        {
            BuildToolsRuntime.Reset();
        }
    }

    [Test]
    [NotInParallel(RuntimeConstraint)]
    public async Task GitHubLogCommands_AreEscaped()
    {
        var lines = new List<string>();

        try
        {
            BuildToolsRuntime.WriteLine = lines.Add;
            var build = new DummyBuild();

            build.GitHubLogGroup("group\nname", () => lines.Add("inside"));
            build.GitHubError("bad\nmessage", "src/File,One.cs", 7, 2);
            build.GitHubWarning("warn%message");
            build.GitHubDebug("debug\rmessage");

            await TAssert.That(lines.SequenceEqual(
            [
                "::group::group%0Aname",
                "inside",
                "::endgroup::",
                "::error file=src/File%2COne.cs,line=7,col=2::bad%0Amessage",
                "::warning::warn%25message",
                "::debug::debug%0Dmessage",
            ])).IsTrue();
        }
        finally
        {
            BuildToolsRuntime.Reset();
        }
    }

    [Test]
    public async Task SetGithubCredentials_ConfiguresClient()
    {
        var previousClient = GitHubTasks.GitHubClient;
        try
        {
            new DummyBuild().SetGithubCredentials("token-value");

            await TAssert.That(GitHubTasks.GitHubClient).IsNotNull();
        }
        finally
        {
            GitHubTasks.GitHubClient = previousClient;
        }
    }

    [Test]
    [NotInParallel(RuntimeConstraint)]
    public async Task GenerateReleaseNotes_FormatsGitLogOutput()
    {
        try
        {
            BuildToolsRuntime.RunGitCommand = arguments =>
            {
                return arguments.Contains("log -n 2", StringComparison.Ordinal)
                    ? ["1234567890abcdef:::Add feature", "abcdef1234567890:::Fix bug"]
                    : [];
            };

            var repo = (GitRepository)RuntimeHelpers.GetUninitializedObject(typeof(GitRepository));

            var notes = repo.GenerateReleaseNotes(2);

            await TAssert.That(notes).IsEqualTo($"### Commits{Environment.NewLine}- Add feature (1234567){Environment.NewLine}- Fix bug (abcdef1){Environment.NewLine}");
        }
        finally
        {
            BuildToolsRuntime.Reset();
        }
    }

    [Test]
    public async Task GenerateReleaseNotes_NullRepoThrows()
    {
        GitRepository? repo = null;

        await TAssert.That(() => repo!.GenerateReleaseNotes()).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ReleaseHelpers_ValidateOrNoOpForOfflinePaths()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var release = (Release)RuntimeHelpers.GetUninitializedObject(typeof(Release));
            GitRepository? repo = null;
            Release? nullRelease = null;

            release.UploadReleaseAssetToGithub((AbsolutePath)Path.Combine(tempDirectory, "missing.zip"));
            var returnedRelease = release.UploadDirectory((AbsolutePath)tempDirectory);

            await TAssert.That(ReferenceEquals(release, returnedRelease)).IsTrue();
            await TAssert.That(() => nullRelease!.AppendReleaseNotes((GitRepository)RuntimeHelpers.GetUninitializedObject(typeof(GitRepository)))).Throws<ArgumentNullException>();
            await TAssert.That(() => new DummyBuild().CreateRelease(repo!, "v1.0.0", "1.0.0", null, false)).Throws<ArgumentNullException>();
            await TAssert.That(() => nullRelease!.UpdateReleaseBody((GitRepository)RuntimeHelpers.GetUninitializedObject(typeof(GitRepository)), "body")).Throws<ArgumentNullException>();
            await TAssert.That(() => nullRelease!.Publish((GitRepository)RuntimeHelpers.GetUninitializedObject(typeof(GitRepository)))).Throws<ArgumentNullException>();
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CP.Nuke.BuildTools.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
