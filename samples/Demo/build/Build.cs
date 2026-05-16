using CP.BuildTools;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Serilog;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

sealed class Build : NukeBuild
{
    public static int Main()
    {
        UseSampleRootDirectory();
        return Execute<Build>(x => x.Default);
    }

    [Parameter("Configuration to build")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Parameter("Optional substring filter matched against solution project names")]
    readonly string? ProjectFilter;

    [Solution]
    readonly Solution Solution = null!;

    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    IEnumerable<Project> SelectedProjects =>
        Solution.AllProjects
            .Where(project => project.Name != "_build")
            .Where(project => string.IsNullOrWhiteSpace(ProjectFilter) ||
                project.Name.Contains(ProjectFilter, StringComparison.OrdinalIgnoreCase));

    Target PrintProjectFilters => _ => _
        .Executes(() =>
        {
            Log.Information("Public NuGet source: {Source}", this.PublicNuGetSource());
            Log.Information("Selected projects: {Projects}", string.Join(", ", SelectedProjects.Select(x => x.Name)));
            var metadata = Solution.Path.ReadSolutionProjectInfos();
            Log.Information("Solution file: {Solution}", Solution.Path);
            Log.Information("Test projects: {Projects}", string.Join(", ", Solution.GetTestProjects().Select(x => x.Name)));
            Log.Information("Packable projects: {Projects}", string.Join(", ", Solution.GetPackableProjects().Select(x => x.Name)));
            Log.Information("Publishable projects: {Projects}", string.Join(", ", metadata.GetPublishableProjectInfos().Select(x => x.Name)));
            Log.Information("Windows desktop projects: {Projects}", string.Join(", ", metadata.Where(x => x.IsWindowsDesktopProject).Select(x => x.Name)));
            Log.Information("MAUI projects: {Projects}", string.Join(", ", metadata.Where(x => x.IsMauiProject).Select(x => x.Name)));
            Log.Information("Shared project found: {Found}", Solution.GetProject("Demo.Shared") is not null);
        });

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() => ArtifactsDirectory.CreateOrCleanDirectory());

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() => DotNetRestore(s => s.SetProjectFile(Solution)));

    Target Compile => _ => _
        .DependsOn(Restore, PrintProjectFilters)
        .Executes(() =>
        {
            foreach (var project in SelectedProjects)
            {
                DotNetBuild(s => s
                    .SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .EnableNoRestore());
            }
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            foreach (var project in Solution.GetTestProjects())
            {
                DotNetTest(s => s
                    .SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .EnableNoBuild()
                    .SetResultsDirectory(ArtifactsDirectory / "test-results"));
            }
        });

    Target Default => _ => _
        .DependsOn(Test);

    static void UseSampleRootDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var parametersPath = Path.Combine(directory.FullName, ".nuke", "parameters.json");
            if (!File.Exists(parametersPath) || !File.ReadAllText(parametersPath).Contains("Demo.slnx", StringComparison.Ordinal))
            {
                continue;
            }

            Directory.SetCurrentDirectory(directory.FullName);
            return;
        }
    }
}
