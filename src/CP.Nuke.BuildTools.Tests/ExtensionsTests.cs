// Copyright (c) Chris Pulman. All rights reserved.
// Licensed under the MIT license.

using System;
using System.IO;
using System.Threading.Tasks;
using CP.BuildTools;
using Nuke.Common; // build base
using Nuke.Common.IO; // AbsolutePath
using NUnit.Framework; // NUnit

namespace CP.Nuke.BuildTools.Tests;

/// <summary>
/// ExtensionsTests.
/// </summary>
[TestFixture]
public class ExtensionsTests
{
    private class DummyBuild : NukeBuild { }

    /// <summary>
    /// Publics the nu get source returns expected.
    /// </summary>
    [Test]
    public void PublicNuGetSource_ReturnsExpected()
    {
        var build = new DummyBuild();
        var src = build.PublicNuGetSource();
        NUnit.Framework.Assert.That(src, NUnit.Framework.Is.EqualTo("https://api.nuget.org/v3/index.json"));
    }

    /// <summary>
    /// Gets the file from URL asynchronous blank URL returns empty string.
    /// </summary>
    [Test]
    public async Task GetFileFromUrlAsync_BlankUrl_ReturnsEmptyString()
    {
        var result = await CP.BuildTools.Extensions.GetFileFromUrlAsync(" ");
        NUnit.Framework.Assert.That(result, NUnit.Framework.Is.EqualTo(string.Empty));
    }

    /// <summary>
    /// Gets the packable projects null solution returns null.
    /// </summary>
    [Test]
    public void GetPackableProjects_NullSolution_ReturnsNull()
    {
        global::Nuke.Common.ProjectModel.Solution? solution = null;
        NUnit.Framework.Assert.That(solution.GetPackableProjects(), NUnit.Framework.Is.Null);
    }

    /// <summary>
    /// Gets the test projects null solution returns null.
    /// </summary>
    [Test]
    public void GetTestProjects_NullSolution_ReturnsNull()
    {
        global::Nuke.Common.ProjectModel.Solution? solution = null;
        NUnit.Framework.Assert.That(solution.GetTestProjects(), NUnit.Framework.Is.Null);
    }

    /// <summary>
    /// Gets the project null solution returns null.
    /// </summary>
    [Test]
    public void GetProject_NullSolution_ReturnsNull()
    {
        global::Nuke.Common.ProjectModel.Solution? solution = null;
        NUnit.Framework.Assert.That(solution.GetProject("Any"), NUnit.Framework.Is.Null);
    }

    /// <summary>
    /// Checkouts the invalid arguments returns without throw.
    /// </summary>
    [Test]
    public void Checkout_InvalidArgs_ReturnsWithoutThrow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var path = (AbsolutePath)tempDir; // rooted
        NUnit.Framework.Assert.DoesNotThrow(() => path.Checkout(string.Empty));
    }

    /// <summary>
    /// Determines whether [is git hub actions false by default].
    /// </summary>
    [Test]
    public void IsGitHubActions_FalseByDefault()
    {
        var build = new DummyBuild();
        NUnit.Framework.Assert.That(build.IsGitHubActions(), NUnit.Framework.Is.False);
    }

    /// <summary>
    /// Gits the hub environment helpers return null when unset.
    /// </summary>
    [Test]
    public void GitHubEnvironmentHelpers_ReturnNullWhenUnset()
    {
        var build = new DummyBuild();
        NUnit.Framework.Assert.That(build.GitHubRepository(), NUnit.Framework.Is.Null);
        NUnit.Framework.Assert.That(build.GitHubRef(), NUnit.Framework.Is.Null);
        NUnit.Framework.Assert.That(build.GitHubSha(), NUnit.Framework.Is.Null);
        NUnit.Framework.Assert.That(build.GitHubActor(), NUnit.Framework.Is.Null);
        NUnit.Framework.Assert.That(build.GitHubWorkspace(), NUnit.Framework.Is.Null);
        NUnit.Framework.Assert.That(build.GitHubRunNumber(), NUnit.Framework.Is.Null);
        NUnit.Framework.Assert.That(build.GitHubRunId(), NUnit.Framework.Is.Null);
    }

    /// <summary>
    /// Gits the hub set output no env no exception.
    /// </summary>
    [Test]
    public void GitHubSetOutput_NoEnv_NoException()
    {
        var build = new DummyBuild();
        NUnit.Framework.Assert.DoesNotThrow(() => build.GitHubSetOutput("name", "value"));
    }

    /// <summary>
    /// Gits the hub append summary no env no exception.
    /// </summary>
    [Test]
    public void GitHubAppendSummary_NoEnv_NoException()
    {
        var build = new DummyBuild();
        NUnit.Framework.Assert.DoesNotThrow(() => build.GitHubAppendSummary("markdown"));
    }

    /// <summary>
    /// Gits the hub log group executes action.
    /// </summary>
    [Test]
    public void GitHubLogGroup_ExecutesAction()
    {
        var build = new DummyBuild();
        var executed = false;
        build.GitHubLogGroup("group", () => executed = true);
        NUnit.Framework.Assert.That(executed, NUnit.Framework.Is.True);
    }

    /// <summary>
    /// Gits the hub error no throw.
    /// </summary>
    [Test]
    public void GitHubError_NoThrow()
    {
        var build = new DummyBuild();
        NUnit.Framework.Assert.DoesNotThrow(() => build.GitHubError("error message"));
    }

    /// <summary>
    /// Gits the hub warning no throw.
    /// </summary>
    [Test]
    public void GitHubWarning_NoThrow()
    {
        var build = new DummyBuild();
        NUnit.Framework.Assert.DoesNotThrow(() => build.GitHubWarning("warn"));
    }

    /// <summary>
    /// Gits the hub debug no throw.
    /// </summary>
    [Test]
    public void GitHubDebug_NoThrow()
    {
        var build = new DummyBuild();
        NUnit.Framework.Assert.DoesNotThrow(() => build.GitHubDebug("debug"));
    }

    /// <summary>
    /// Generates the release notes null repo throws.
    /// </summary>
    [Test]
    public void GenerateReleaseNotes_NullRepo_Throws()
    {
        global::Nuke.Common.Git.GitRepository? repo = null;
        NUnit.Framework.Assert.Throws<ArgumentNullException>(() => repo.GenerateReleaseNotes());
    }

    /// <summary>
    /// Saves the file writes when not exists.
    /// </summary>
    [Test]
    public void SaveFile_WritesWhenNotExists()
    {
        var build = new DummyBuild();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var filePath = (AbsolutePath)Path.Combine(tempDir, "test.bin");
        var data = new byte[] { 1, 2, 3 };
        // Nuke's AbsolutePath.Exists() throws if ambiguous; ensure file doesn't exist yet
        NUnit.Framework.Assert.False(File.Exists(filePath));
        build.SaveFile(filePath, data);
        NUnit.Framework.Assert.True(File.Exists(filePath));
        NUnit.Framework.CollectionAssert.AreEqual(data, File.ReadAllBytes(filePath));
    }

    /// <summary>
    /// Saves the file skip when exists.
    /// </summary>
    [Test]
    public void SaveFile_SkipWhenExists()
    {
        var build = new DummyBuild();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var filePath = (AbsolutePath)Path.Combine(tempDir, "test.bin");
        var original = new byte[] { 9, 8, 7 };
        File.WriteAllBytes(filePath, original);
        build.SaveFile(filePath, new byte[] { 1, 2, 3 });
        // content unchanged
        NUnit.Framework.CollectionAssert.AreEqual(original, File.ReadAllBytes(filePath));
    }
}
