using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace SteamBacklogPicker.UI.Tests;

public sealed class AccessibilityChecklistTests
{
    [Fact]
    public void ChecklistDocumentExistsWithMandatorySections()
    {
        var checklistPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..",
            "docs", "accessibility-checklist.md"));

        File.Exists(checklistPath).Should().BeTrue("o checklist de acessibilidade deve acompanhar o projeto");

        var content = File.ReadAllText(checklistPath);
        content.Should().Contain("# Checklist de Acessibilidade");
        content.Should().Contain("## Controles interativos");
        content.Should().Contain("## Comunicação assistiva");
    }
}
