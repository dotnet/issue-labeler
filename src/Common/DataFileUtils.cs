// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public static class DataFileUtils
{
    // Create an output file's directory (recursively)
    public static void EnsureOutputDirectory(string outputFile)
    {
        string? outputDir = Path.GetDirectoryName(outputFile);

        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }
    }

    // Ensure text written into tab-separated format is collapsed onto a single line
    // without any false-positive matches for tabs, and ensuring quotes don't lead
    // to the tab separator characters being unrecognized.
    public static string SanitizeText(string text) => text
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Replace('\t', ' ')
        .Replace('"', '`');

    // Turn arrays of strings into space-separated values
    public static string SanitizeTextArray(string[] texts) => string.Join(" ", texts.Select(SanitizeText));

    // Format an issue record into tab-separated format
    public static string FormatIssueRecord(string label, string title, string body) =>
        string.Join('\t', [
            SanitizeText(label),
            SanitizeText(title),
            SanitizeText(body)
        ]);

    // Format a pull request record into tab-separated format
    public static string FormatPullRequestRecord(string label, string title, string body, string[] fileNames, string[] folderNames) =>
        string.Join('\t', [
            SanitizeText(label),
            SanitizeText(title),
            SanitizeText(body),
            SanitizeTextArray(fileNames),
            SanitizeTextArray(folderNames)
        ]);
}
