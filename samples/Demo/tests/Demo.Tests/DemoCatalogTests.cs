using Demo.Shared;

namespace Demo.Tests;

public sealed class DemoCatalogTests
{
    [Test]
    public async Task CurrentProfileNamesTheDemo()
    {
        await Assert.That(DemoCatalog.Current.Name).Contains("CP.Nuke.BuildTools");
    }

    [Test]
    public async Task ProjectTypesIncludeWindowsDesktopExamples()
    {
        await Assert.That(DemoCatalog.ProjectTypes).Contains("WPF");
        await Assert.That(DemoCatalog.ProjectTypes).Contains("WinForms");
    }
}
