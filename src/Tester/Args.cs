// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

public struct Args
{
    public string? Org { get; set; }
    public List<string> Repos { get; set; }
    public string? GithubToken { get; set; }
    public string? IssueDataPath { get; set; }
    public string? IssueModelPath { get; set; }
    public int? IssueLimit { get; set; }
    public string? PullDataPath { get; set; }
    public string? PullModelPath { get; set; }
    public int? PullLimit { get; set; }
    public float? Threshold { get; set; }
    public Predicate<string> LabelPredicate { get; set; }
    public string[]? ExcludedAuthors { get; set; }

    static void ShowUsage(string? message = null)
    {
        // The entire condition is used to determine if the configuration is invalid.
        // If any of the following are true, the configuration is considered invalid:
        // • The LabelPredicate is null.
        // • Both IssueDataPath and PullDataPath are null, and either Org, Repos, or GithubToken is null.
        // • Both IssueModelPath and PullModelPath are null.

        string executableName = Process.GetCurrentProcess().ProcessName;

        Console.WriteLine($$"""
            ERROR: Invalid or missing arguments.{{(message is null ? "" : " " + message)}}

            Usage:
              {{executableName}} --repo {org/repo1}[,{org/repo2},...] --label-prefix {label-prefix} [options]

                Required arguments:
                  --repo              The GitHub repositories in format org/repo (comma separated for multiple).
                  --label-prefix      Prefix for label predictions. Must end with a character other than a letter or number.

                Required for testing the issue model:
                  --issue-data        Path to existing issue data file (TSV file).
                  --issue-model       Path to existing issue prediction model file (ZIP file).

                Required for testing the pull request model:
                  --pull-data         Path to existing pull request data file (TSV file).
                  --pull-model        Path to existing pull request prediction model file (ZIP file).

                Optional arguments:
                  --threshold         Minimum prediction confidence threshold. Range (0,1]. Default 0.4.
                  --issue-limit       Maximum number of issues to download. Default: No limit.
                  --pull-limit        Maximum number of pull requests to download. Default: No limit.
                  --excluded-authors  Comma-separated list of authors to exclude.
                  --token             GitHub access token. Default: read from GITHUB_TOKEN env var.
            """);


        Environment.Exit(1);
    }

    public static Args? Parse(string[] args)
    {
        Args argsData = new()
        {
            Threshold = 0.4f
        };

        Queue<string> arguments = new(args);
        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (argument)
            {
                case "--token":
                    if (!ArgUtils.TryDequeueString(arguments, ShowUsage, "--token", out string? token))
                    {
                        return null;
                    }
                    argsData.GithubToken = token;
                    break;

                case "--repo":
                    if (!ArgUtils.TryDequeueRepoList(arguments, ShowUsage, "--repo", out string? org, out List<string>? repos))
                    {
                        return null;
                    }
                    argsData.Org = org;
                    argsData.Repos = repos;
                    break;

                case "--issue-data":
                    if (!ArgUtils.TryDequeuePath(arguments, ShowUsage, "--issue-data", out string? issueDataPath))
                    {
                        return null;
                    }
                    argsData.IssueDataPath = issueDataPath;
                    break;

                case "--issue-model":
                    if (!ArgUtils.TryDequeuePath(arguments, ShowUsage, "--issue-model", out string? issueModelPath))
                    {
                        return null;
                    }
                    argsData.IssueModelPath = issueModelPath;
                    break;

                case "--issue-limit":
                    if (!ArgUtils.TryDequeueInt(arguments, ShowUsage, "--issue-limit", out int? issueLimit))
                    {
                        return null;
                    }
                    argsData.IssueLimit = issueLimit;
                    break;

                case "--pull-data":
                    if (!ArgUtils.TryDequeuePath(arguments, ShowUsage, "--pull-data", out string? pullDataPath))
                    {
                        return null;
                    }
                    argsData.PullDataPath = pullDataPath;
                    break;

                case "--pull-model":
                    if (!ArgUtils.TryDequeuePath(arguments, ShowUsage, "--pull-model", out string? pullModelPath))
                    {
                        return null;
                    }
                    argsData.PullModelPath = pullModelPath;
                    break;

                case "--pull-limit":
                    if (!ArgUtils.TryDequeueInt(arguments, ShowUsage, "--pull-limit", out int? pullLimit))
                    {
                        return null;
                    }
                    argsData.PullLimit = pullLimit;
                    break;

                case "--label-prefix":
                    if (!ArgUtils.TryDequeueLabelPrefix(arguments, ShowUsage, "--label-prefix", out Func<string, bool>? labelPredicate))
                    {
                        return null;
                    }
                    argsData.LabelPredicate = new(labelPredicate);
                    break;

                case "--threshold":
                    if (!ArgUtils.TryDequeueFloat(arguments, ShowUsage, "--threshold", out float? threshold))
                    {
                        return null;
                    }
                    argsData.Threshold = threshold.Value;
                    break;

                case "--excluded-authors":
                    if (!ArgUtils.TryDequeueStringArray(arguments, ShowUsage, "--excluded-authors", out string[]? excludedAuthors))
                    {
                        return null;
                    }
                    argsData.ExcludedAuthors = excludedAuthors;
                    break;

                default:
                    ShowUsage($"Unrecognized argument: {argument}");
                    return null;
            }
        }

        if (argsData.LabelPredicate is null ||
            (
                argsData.IssueDataPath is null && argsData.PullDataPath is null &&
                (argsData.Org is null || argsData.Repos.Count == 0 || argsData.GithubToken is null)
            ) ||
            (argsData.IssueModelPath is null && argsData.PullModelPath is null)
        )
        {
            ShowUsage();
            return null;
        }

        return argsData;
    }
}
