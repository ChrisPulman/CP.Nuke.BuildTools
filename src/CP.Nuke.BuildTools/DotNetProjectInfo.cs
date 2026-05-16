// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Nuke.Common.IO;

namespace CP.BuildTools;

/// <summary>
/// Describes the build-relevant metadata discovered from an SDK-style .NET project file.
/// </summary>
/// <param name="Path">The project file path.</param>
/// <param name="Name">The project name.</param>
/// <param name="Sdk">The SDK declared by the project.</param>
/// <param name="TargetFrameworks">The target frameworks declared by the project.</param>
/// <param name="RuntimeIdentifiers">The runtime identifiers declared by the project.</param>
/// <param name="PackageReferences">The package references declared by the project.</param>
/// <param name="ProjectReferences">The project references declared by the project.</param>
/// <param name="OutputType">The output type.</param>
/// <param name="IsPackable">A value indicating whether the project is packable.</param>
/// <param name="IsTestProject">A value indicating whether the project is a test project.</param>
/// <param name="IsPublishable">A value indicating whether the project is usually publishable.</param>
/// <param name="IsExecutable">A value indicating whether the project produces an executable output.</param>
/// <param name="IsWebProject">A value indicating whether the project uses the ASP.NET Core web SDK.</param>
/// <param name="IsWorkerProject">A value indicating whether the project uses the worker SDK.</param>
/// <param name="IsWindowsDesktopProject">A value indicating whether the project is a WPF or WinForms project.</param>
/// <param name="IsMauiProject">A value indicating whether the project uses .NET MAUI.</param>
/// <param name="IsContainerEnabled">A value indicating whether SDK container publishing appears enabled.</param>
public sealed record DotNetProjectInfo(
    AbsolutePath Path,
    string Name,
    string Sdk,
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<string> RuntimeIdentifiers,
    IReadOnlyList<string> PackageReferences,
    IReadOnlyList<AbsolutePath> ProjectReferences,
    string? OutputType,
    bool IsPackable,
    bool IsTestProject,
    bool IsPublishable,
    bool IsExecutable,
    bool IsWebProject,
    bool IsWorkerProject,
    bool IsWindowsDesktopProject,
    bool IsMauiProject,
    bool IsContainerEnabled);
