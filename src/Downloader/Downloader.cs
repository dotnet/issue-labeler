// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using static DataFileUtils;
using GitHubClient;

if (ConfigurationParser.Parse(args) is not Configuration argsData)
{
    return;
}

List<Task> tasks = [];

if (!string.IsNullOrEmpty(argsData.IssuesPath))
{
    EnsureOutputDirectory(argsData.IssuesPath);
    tasks.Add(Task.Run(() => DownloadIssues(argsData.IssuesPath)));
}

if (!string.IsNullOrEmpty(argsData.PullsPath))
{
    EnsureOutputDirectory(argsData.PullsPath);
    tasks.Add(Task.Run(() => DownloadPullRequests(argsData.PullsPath)));
}

await Task.WhenAll(tasks);

async Task DownloadIssues(string outputPath)
{
    Console.WriteLine($"Issues Data Path: {outputPath}");

    byte perFlushCount = 0;

    using StreamWriter writer = new StreamWriter(outputPath);
    writer.WriteLine(FormatIssueRecord("Label", "Title", "Body"));

    foreach (var repo in argsData.Repos)
    {
        await foreach (var result in GitHubApi.DownloadIssues(argsData.GithubToken, argsData.Org, repo, argsData.LabelPredicate, argsData.IssueLimit, argsData.PageSize ?? 100, argsData.PageLimit ?? 1000, argsData.Retries, argsData.Verbose))
        {
            writer.WriteLine(FormatIssueRecord(result.Label, result.Issue.Title, result.Issue.Body));

            if (++perFlushCount == 100)
            {
                writer.Flush();
                perFlushCount = 0;
            }
        }
    }

    writer.Close();
}

async Task DownloadPullRequests(string outputPath)
{
    Console.WriteLine($"Pulls Data Path: {outputPath}");

    byte perFlushCount = 0;

    using StreamWriter writer = new StreamWriter(outputPath);
    writer.WriteLine(FormatPullRequestRecord("Label", "Title", "Body", ["FileNames"], ["FolderNames"]));

    foreach (var repo in argsData.Repos)
    {
        await foreach (var result in GitHubApi.DownloadPullRequests(argsData.GithubToken, argsData.Org, repo, argsData.LabelPredicate, argsData.PullLimit, argsData.PageSize ?? 25, argsData.PageLimit ?? 4000, argsData.Retries, argsData.Verbose))
        {
            writer.WriteLine(FormatPullRequestRecord(result.Label, result.PullRequest.Title, result.PullRequest.Body, result.PullRequest.FileNames, result.PullRequest.FolderNames));

            if (++perFlushCount == 100)
            {
                writer.Flush();
                perFlushCount = 0;
            }
        }
    }

    writer.Close();
}
