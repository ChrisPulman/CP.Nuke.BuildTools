![License](https://img.shields.io/github/license/ChrisPulman/CP.Nuke.BuildTools.svg) [![Build](https://github.com/ChrisPulman/CP.Nuke.BuildTools/actions/workflows/BuildOnly.yml/badge.svg)](https://github.com/ChrisPulman/CP.Nuke.BuildTools/actions/workflows/BuildOnly.yml) ![Nuget](https://img.shields.io/nuget/dt/CP.Nuke.BuildTools?color=pink&style=plastic) [![NuGet](https://img.shields.io/nuget/v/CP.Nuke.BuildTools.svg?style=plastic)](https://www.nuget.org/packages/CP.Nuke.BuildTools)

![Alt](https://repobeats.axiom.co/api/embed/eee7bd264c6d9519dff01174ca8a6642ad0fa6a9.svg "Repobeats analytics image")

# CP.Nuke.BuildTools
A collection of extension methods that simplify common build, packaging and release tasks when using [Nuke Build](https://nuke.build/).

Add the NuGet package to your build project (the one containing `Build.cs`):
```
dotnet add build/_build.csproj package CP.Nuke.BuildTools
```
Then add:
```csharp
using CP.BuildTools; // at top of Build.cs
```

## Extension Overview
The following extension methods are available (defined in `build/Extensions.cs`):

| Method | Purpose |
|--------|---------|
| `PublicNuGetSource()` | Returns the public NuGet v3 index URL. |
| `UpdateVisualStudio(version)` | Updates VS installation and applies common workloads. |
| `GetFileFromUrlAsync(url)` | Downloads text content from a URL. Returns empty string on blank URL. |
| `RestoreProjectWorkload(project)` | Runs `dotnet workload restore` for a single project. |
| `RestoreSolutionWorkloads(solution)` | Runs `dotnet workload restore` for the whole solution. |
| `GetPackableProjects(solution)` | Returns projects with `IsPackable` MSBuild property true. |
| `GetTestProjects(solution)` | Returns projects with `IsTestProject` MSBuild property true. |
| `InstallDotNetSdk(params versions)` | Installs SDK channels matching version patterns (e.g. `8.x.x`, `9.0.100`). |
| `GetAsset(repoOwner,repoName,assetName,releaseTag)` | Downloads a release asset from GitHub. |
| `SaveFile(path,bytes)` | Writes a file if it does not already exist. |
| `SetGithubCredentials(token)` | Sets credentials on Octokit client for later GitHub API calls. |
| `UploadReleaseAssetToGithub(release,assetPath)` | Uploads (or replaces) a single asset file into a draft release. |
| `UploadDirectory(release,directory)` | Uploads all root files in a directory as release assets. |
| `CreateRelease(repo,tag,version,commitSha,isPrerelease)` | Creates a draft GitHub release. |
| `AppendReleaseNotes(release,repo,maxCommits)` | Appends generated commit list to release body. |
| `Publish(release,repo)` | Publishes (un-drafts) a release. |
| `InstallAspNetCore(version)` | Installs ASP.NET Core runtime for a channel (>= 6). |
| `GenerateReleaseNotes(repo,maxCommits)` | Returns markdown listing recent commits. |

> NOTE: Methods that execute external processes (`ProcessTasks.StartShell`) or call GitHub API have side-effects and expect required tools (git, pwsh) and environment (GitHub PAT) to be available.

## Usage Examples
Below are example targets you can integrate into your `Build` class.

### 1. Basic NuGet Source
```csharp
Target Info => _ => _
    .Executes(() =>
    {
        Log.Information("NuGet V3: {Source}", this.PublicNuGetSource());
    });
```

### 2. Update Visual Studio & Workloads (CI environments)
```csharp
Target SetupCI => _ => _
    .Executes(async () =>
    {
        await this.UpdateVisualStudio(); // Enterprise by default
        await this.InstallDotNetSdk("8.x.x", "9.x.x"); // install latest matching channels
        this.InstallAspNetCore("8.0");
        Solution.RestoreSolutionWorkloads();
    });
```

### 3. Packing Projects
```csharp
Target Pack => _ => _
    .DependsOn(Compile)
    .Executes(() =>
    {
        var packable = Solution.GetPackableProjects();
        if (packable == null || packable.Count == 0)
        {
            Log.Warning("No packable projects found.");
            return;
        }
        DotNetPack(s => s
            .SetConfiguration(Configuration)
            .CombineWith(packable, (ps, p) => ps.SetProject(p)));
    });
```

### 4. Installing Specific SDK Channels
```csharp
Target InstallSdks => _ => _
    .Executes(async () =>
    {
        // Patterns: Major.Minor.Patch can use 'x' placeholders.
        await this.InstallDotNetSdk("6.x.x", "7.x.x", "8.0.x", "9.0.100");
    });
```

### 5. GitHub Release Automation
Requires a GitHub PAT exported as an environment variable (e.g. `GITHUB_TOKEN`).
```csharp
[Parameter][Secret] readonly string GitHubToken;
[GitRepository] readonly GitRepository Repository;

Target Release => _ => _
    .Requires(() => GitHubToken)
    .Executes(() =>
    {
        this.SetGithubCredentials(GitHubToken);

        // Create draft release
        var tag = $"v{NerdbankVersioning.NuGetPackageVersion}";
        var release = this.CreateRelease(Repository, tag, NerdbankVersioning.NuGetPackageVersion, null, isPrerelease: false);

        // Append commit based notes
        release = release.AppendReleaseNotes(Repository, maxCommits: 40);

        // Upload artifacts (assumes Pack target already produced nupkgs)
        var artifactsDir = RootDirectory / "output";
        release.UploadDirectory(artifactsDir);

        // Publish
        release.Publish(Repository);
    });
```

### 6. Downloading a Release Asset
```csharp
Target FetchAsset => _ => _
    .Requires(() => GitHubToken)
    .Executes(() =>
    {
        this.SetGithubCredentials(GitHubToken);
        var bytes = this.GetAsset("owner", "repo", "mytool.zip", uiReleaseTag: null); // latest release
        var dest = RootDirectory / "temp" / "mytool.zip";
        this.SaveFile(dest, bytes);
        Log.Information("Saved asset to {Path}", dest);
    });
```

### 7. Using Commit Release Notes Separately
```csharp
Target Notes => _ => _
    .Executes(() =>
    {
        var markdown = Repository.GenerateReleaseNotes(25);
        File.WriteAllText(RootDirectory / "RELEASE_NOTES.md", markdown);
    });
```

### 8. Clone External Repository (Shallow)
```csharp
Target CloneExample => _ => _
    .Executes(() =>
    {
        var targetDir = RootDirectory / "external";
        targetDir.CreateDirectory();
        targetDir.Checkout("https://github.com/owner/repo.git");
    });
```

## Error Handling & Notes
- `InstallDotNetSdk` throws if no matching stable versions are found.
- `InstallAspNetCore` throws if version < 6 or cannot parse.
- `CreateRelease`, `Publish`, `AppendReleaseNotes` require a valid authenticated Octokit client (set via `SetGithubCredentials`).
- Network dependent methods (`GetFileFromUrlAsync`, GitHub operations) should be wrapped with retry logic if used in unstable environments.

## Suggested Target Ordering
A typical pipeline using these utilities:
1. `SetupCI` – ensure tooling & SDKs.
2. `Restore` (standard Nuke restore + workloads if needed).
3. `Compile`.
4. `Pack`.
5. `Release` (optional for main / tagged builds).

## License
MIT – see LICENSE file.
