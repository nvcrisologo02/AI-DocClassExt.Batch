using System.IO;
using DocumentIA.Batch.ClassificationLite.Services;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class ManualLauncherTests
{
    [Fact]
    public void ExtractManual_DevuelveRutaConHtmlDelManual()
    {
        var path = ManualLauncher.ExtractManual();

        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("<html", content, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Batch Classification Lite", content);
    }
}
