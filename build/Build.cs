using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.NerdbankGitVersioning;
using Nuke.Common.Utilities.Collections;
using Serilog;
using CP.Nuke.BuildTools;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using Nuke.Common.Tools.DotNet;

[GitHubActions(
    "BuildOnly",
    GitHubActionsImage.WindowsLatest,
    OnPushBranchesIgnore = new[] { "main" },
    FetchDepth = 0,
    InvokedTargets = new[] { nameof(Compile) })]
[GitHubActions(
    "BuildDeploy",
    GitHubActionsImage.WindowsLatest,
    OnPushBranches = new[] { "main" },
    FetchDepth = 0,
    ImportSecrets = new[] { nameof(NuGetApiKey) },
    InvokedTargets = new[] { nameof(Compile), nameof(Deploy) })]
class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main() => Execute<Build>(x => x.Compile);

    [GitRepository] readonly GitRepository Repository;
    [Solution(GenerateProjects = true)] readonly Solution Solution;
    [NerdbankGitVersioning] readonly NerdbankGitVersioning NerdbankVersioning;
    [Parameter][Secret] readonly string NuGetApiKey;

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;
    AbsolutePath PackagesDirectory => RootDirectory / "output";

    Target Print => _ => _
        .Executes(() =>
        {
            Log.Information("NerdbankVersioning = {Value}", NerdbankVersioning.NuGetPackageVersion);
        });

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            if (IsLocalBuild)
            {
                return;
            }

            PackagesDirectory.CreateOrCleanDirectory();
            this.UpdateVisualStudio();
            this.InstallDotNetSdk("6.x.x", "7.x.x");
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            DotNetRestore(s => s.SetProjectFile(Solution));

            Solution.RestoreSolutionWorkloads();
        });

    Target Compile => _ => _
        .DependsOn(Restore, Print)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoRestore());
        });

    Target Pack => _ => _
    .After(Compile)
    .Produces(PackagesDirectory / "*.nupkg")
    .Executes(() =>
    {
        if (Repository.IsOnMainOrMasterBranch())
        {
            var packableProjects = Solution.GetPackableProjects();

            packableProjects.ForEach(project =>
            {
                Log.Information("Restoring workloads of {Input}", project);
                project.RestoreProjectWorkload();
            });

            DotNetPack(settings => settings
                .SetConfiguration(Configuration)
                .SetVersion(NerdbankVersioning.NuGetPackageVersion)
                .SetOutputDirectory(PackagesDirectory)
                .CombineWith(packableProjects, (packSettings, project) =>
                    packSettings.SetProject(project)));
        }
        else
        {
            Log.Information("Skipping pack because we are not on main or master branch");
        }
    });

    Target Deploy => _ => _
    .DependsOn(Pack)
    .Requires(() => NuGetApiKey)
    .Executes(() =>
    {
        if (Repository.IsOnMainOrMasterBranch())
        {
            DotNetNuGetPush(settings => settings
                        .SetSkipDuplicate(true)
                        .SetSource(this.PublicNuGetSource())
                        .SetApiKey(NuGetApiKey)
                        .CombineWith(PackagesDirectory.GlobFiles("*.nupkg"), (s, v) => s.SetTargetPath(v)),
                    degreeOfParallelism: 5, completeOnFailure: true);
        }
        else
        {
            Log.Information("Skipping deploy because we are not on main or master branch");
        }
    });
}
