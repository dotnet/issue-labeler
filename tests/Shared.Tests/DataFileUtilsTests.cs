using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Shared.Tests;

[TestClass]
public class DataFileUtilsTests
{
    [TestMethod]
    public void EnsureOutputDirectory_ShouldCreateDirectory_WhenDirectoryDoesNotExist()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var outputFile = Path.Combine(tempPath, "file.txt");

        DataFileUtils.EnsureOutputDirectory(outputFile);

        Assert.IsTrue(Directory.Exists(tempPath));

        Directory.Delete(tempPath, true);
    }

    [TestMethod]
    public void SanitizeText_ShouldReplaceSpecialCharacters()
    {
        var input = "text\rwith\nspecial\tcharacters\"";
        var expected = "text with special characters`";

        var result = DataFileUtils.SanitizeText(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void SanitizeTextArray_ShouldJoinSanitizedTexts()
    {
        var input = new[] { "text1", "text2\r\n", "text3\t" };
        var expected = "text1 text2 text3";

        var result = DataFileUtils.SanitizeTextArray(input);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void FormatIssueRecord_ShouldFormatCorrectly()
    {
        var label = "bug";
        var title = "Issue title";
        var body = "Issue body";
        var expected = "bug\tIssue title\tIssue body";

        var result = DataFileUtils.FormatIssueRecord(label, title, body);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void FormatPullRequestRecord_ShouldFormatCorrectly()
    {
        var label = "enhancement";
        var title = "PR title";
        var body = "PR body";
        var fileNames = new[] { "file1.cs", "file2.cs" };
        var folderNames = new[] { "folder1", "folder2" };
        var expected = "enhancement\tPR title\tPR body\tfile1.cs file2.cs\tfolder1 folder2";

        var result = DataFileUtils.FormatPullRequestRecord(label, title, body, fileNames, folderNames);

        Assert.AreEqual(expected, result);
    }
}
