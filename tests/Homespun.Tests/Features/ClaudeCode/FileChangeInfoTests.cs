using Homespun.Features.ClaudeCode.Data;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class FileChangeInfoTests
{
    #region Model Creation Tests

    [Test]
    public void FileChangeInfo_CreatesWithRequiredProperties()
    {
        // Arrange & Act
        var file = new FileChangeInfo
        {
            FilePath = "src/Components/Button.cs",
            Additions = 10,
            Deletions = 5,
            Status = FileChangeStatus.Modified
        };

        // Assert
        Assert.That(file.FilePath, Is.EqualTo("src/Components/Button.cs"));
        Assert.That(file.Additions, Is.EqualTo(10));
        Assert.That(file.Deletions, Is.EqualTo(5));
        Assert.That(file.Status, Is.EqualTo(FileChangeStatus.Modified));
    }

    [Test]
    public void FileChangeInfo_DefaultsToZeroChanges()
    {
        // Arrange & Act
        var file = new FileChangeInfo
        {
            FilePath = "readme.md",
            Status = FileChangeStatus.Modified
        };

        // Assert
        Assert.That(file.Additions, Is.EqualTo(0));
        Assert.That(file.Deletions, Is.EqualTo(0));
    }

    [Test]
    public void FileChangeInfo_SupportsAllStatuses()
    {
        // Arrange & Act
        var added = new FileChangeInfo { FilePath = "new.cs", Status = FileChangeStatus.Added };
        var modified = new FileChangeInfo { FilePath = "existing.cs", Status = FileChangeStatus.Modified };
        var deleted = new FileChangeInfo { FilePath = "old.cs", Status = FileChangeStatus.Deleted };
        var renamed = new FileChangeInfo { FilePath = "renamed.cs", Status = FileChangeStatus.Renamed };

        // Assert
        Assert.That(added.Status, Is.EqualTo(FileChangeStatus.Added));
        Assert.That(modified.Status, Is.EqualTo(FileChangeStatus.Modified));
        Assert.That(deleted.Status, Is.EqualTo(FileChangeStatus.Deleted));
        Assert.That(renamed.Status, Is.EqualTo(FileChangeStatus.Renamed));
    }

    #endregion

    #region FileChangeStatus Enum Tests

    [Test]
    public void FileChangeStatus_HasExpectedValues()
    {
        // Assert
        Assert.That(Enum.GetValues<FileChangeStatus>(), Has.Length.EqualTo(4));
        Assert.That(Enum.IsDefined(FileChangeStatus.Added), Is.True);
        Assert.That(Enum.IsDefined(FileChangeStatus.Modified), Is.True);
        Assert.That(Enum.IsDefined(FileChangeStatus.Deleted), Is.True);
        Assert.That(Enum.IsDefined(FileChangeStatus.Renamed), Is.True);
    }

    [Test]
    public void FileChangeStatus_FromString_ParsesCorrectly()
    {
        // Act
        var added = Enum.Parse<FileChangeStatus>("Added", ignoreCase: true);
        var modified = Enum.Parse<FileChangeStatus>("Modified", ignoreCase: true);
        var deleted = Enum.Parse<FileChangeStatus>("Deleted", ignoreCase: true);
        var renamed = Enum.Parse<FileChangeStatus>("Renamed", ignoreCase: true);

        // Assert
        Assert.That(added, Is.EqualTo(FileChangeStatus.Added));
        Assert.That(modified, Is.EqualTo(FileChangeStatus.Modified));
        Assert.That(deleted, Is.EqualTo(FileChangeStatus.Deleted));
        Assert.That(renamed, Is.EqualTo(FileChangeStatus.Renamed));
    }

    #endregion

    #region Edge Case Tests

    [Test]
    public void FileChangeInfo_AllowsLargeNumbers()
    {
        // Arrange & Act
        var file = new FileChangeInfo
        {
            FilePath = "bigfile.cs",
            Additions = 10000,
            Deletions = 5000,
            Status = FileChangeStatus.Modified
        };

        // Assert
        Assert.That(file.Additions, Is.EqualTo(10000));
        Assert.That(file.Deletions, Is.EqualTo(5000));
    }

    [Test]
    public void FileChangeInfo_AllowsPathsWithSpaces()
    {
        // Arrange & Act
        var file = new FileChangeInfo
        {
            FilePath = "src/My Components/Button Component.cs",
            Status = FileChangeStatus.Modified
        };

        // Assert
        Assert.That(file.FilePath, Is.EqualTo("src/My Components/Button Component.cs"));
    }

    [Test]
    public void FileChangeInfo_AllowsUnicodePaths()
    {
        // Arrange & Act
        var file = new FileChangeInfo
        {
            FilePath = "src/组件/按钮.cs",
            Status = FileChangeStatus.Added
        };

        // Assert
        Assert.That(file.FilePath, Is.EqualTo("src/组件/按钮.cs"));
    }

    #endregion
}
