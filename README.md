![License](https://img.shields.io/github/license/ChrisPulman/CP.Nuke.BuildTools.svg) [![Build](https://github.com/ChrisPulman/CP.Nuke.BuildTools/actions/workflows/BuildOnly.yml/badge.svg)](https://github.com/ChrisPulman/CP.Nuke.BuildTools/actions/workflows/BuildOnly.yml) ![Nuget](https://img.shields.io/nuget/dt/CP.Nuke.BuildTools?color=pink&style=plastic) [![NuGet](https://img.shields.io/nuget/v/CP.Nuke.BuildTools.svg?style=plastic)](https://www.nuget.org/packages/CP.Nuke.BuildTools)

# CP.Nuke.BuildTools

`CP.Nuke.BuildTools` adds pragmatic helpers around [NUKE](https://nuke.build/) for modern .NET cloud builds. The package tracks current `Nuke.Common` usage while filling gaps around `.slnx` discovery, project metadata, GitHub Actions output, and build automation ergonomics.

## Install

Add the package to your NUKE build project, usually `build/_build.csproj`:

```powershell
dotnet add build/_build.csproj package CP.Nuke.BuildTools
```

Then import the namespace in `Build.cs`:

```csharp
using CP.BuildTools;
```

## Current Focus

- TUnit tests running through Microsoft Testing Platform.
- `.slnx`-aware solution discovery and project listing without replacing NUKE's own solution model.
- Project metadata helpers for packable, test, executable, publishable, web, worker, Windows desktop, MAUI, container-enabled, target framework, runtime identifier, package reference, and project reference checks.
- Safer GitHub Actions helpers, including multiline-safe `GITHUB_OUTPUT` writes and escaped log annotations.
- Cache key patterns that include `.slnx`, solution files, project files, props/targets, package management, lock files, and NuGet config.
- NUKE workload restore wrappers using `DotNetWorkloadRestore` instead of ad hoc shell commands.

## Common Helpers

| Method | Purpose |
| --- | --- |
| `PublicNuGetSource()` | Returns the public NuGet v3 source URL. |
| `ResolveSolutionFile(...)` | Finds a single `.slnx`/`.sln`, preferring `.slnx` when appropriate. |
| `DiscoverSolutionFiles(...)` | Lists solution files under a root directory. |
| `ListSolutionProjectPaths(...)` | Uses `dotnet sln list` for `.slnx`/`.sln` project paths. |
| `ReadProjectInfo(...)` | Reads SDK-style project metadata from a project file. |
| `ReadSolutionProjectInfos(...)` | Reads metadata for projects in a solution. |
| `GetPackableProjects()` / `GetTestProjects()` | Filters NUKE solution projects. |
| `GetPublishableProjectInfos()` | Finds publishable app-like projects from metadata. |
| `GitHubSetOutput()` | Writes single-line or multiline GitHub Actions outputs safely. |
| `GitHubActionsHashFilesExpression()` | Produces an Actions `hashFiles(...)` expression using modern cache inputs. |
| `RestoreProjectWorkload()` / `RestoreSolutionWorkloads()` | Runs `dotnet workload restore` through NUKE's DotNet wrapper. |

## Example

```csharp
[Solution] readonly Solution Solution = null!;

Target Info => _ => _
    .Executes(() =>
    {
        var projectInfos = Solution.Path.ReadSolutionProjectInfos();

        Log.Information("NuGet: {Source}", this.PublicNuGetSource());
        Log.Information("Tests: {Projects}", string.Join(", ", Solution.GetTestProjects().Select(x => x.Name)));
        Log.Information("Publishable: {Projects}", string.Join(", ", projectInfos.GetPublishableProjectInfos().Select(x => x.Name)));
    });
```

## Demo

A multi-project demo lives in `samples/Demo`. It includes Console, ASP.NET Web API, Blazor, Worker, gRPC, WPF, WinForms, MAUI Windows, Avalonia, a shared library, and TUnit tests.

Run it from the repository root:

```powershell
dotnet run --project samples/Demo/build/_build.csproj -- --target Default
```

The demo uses `samples/Demo/Demo.slnx` and its NUKE build prints project filters from this package before building and testing the sample graph.

## Test Platform

The repository uses TUnit with Microsoft Testing Platform. `global.json` opts `dotnet test` into the MTP runner for .NET 10 SDKs:

```powershell
dotnet test src/CP.Nuke.BuildTools.slnx
```

## Notes

- `Nuke.Common` remains the source of truth for solution/project loading; this package adds discovery and metadata helpers around it.
- Methods that update Visual Studio, install SDKs/runtimes, call GitHub APIs, or upload release assets have external side effects and should be used intentionally in CI.
- The demo currently restores with an Avalonia transitive advisory warning for `Tmds.DBus.Protocol` from the Avalonia package graph.

## License

MIT. See `LICENSE`.
