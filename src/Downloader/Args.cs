// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

public struct Args
{
    public string Org { get; set; }
    public List<string> Repos { get; set; }
    public string GithubToken { get; set; }
    public string? IssueDataPath { get; set; }
    public int? IssueLimit { get; set; }
    public string? PullDataPath { get; set; }
    public int? PullLimit { get; set; }
    public int? PageSize { get; set; }
    public int? PageLimit { get; set; }
    public int[] Retries { get; set; }
    public Predicate<string> LabelPredicate { get; set; }
    public bool Verbose { get; set; }

    static void ShowUsage(string? message = null)
    {
        string executableName = Process.GetCurrentProcess().ProcessName;

        Console.WriteLine($$"""
            ERROR: Invalid or missing arguments.{{(message is null ? "" : " " + message)}}

            Usage:
              {{executableName}} --repo {org/repo1}[,{org/repo2},...] --label-prefix {label-prefix} [options]

              Required arguments:
                --repo              The GitHub repositories in format org/repo (comma separated for multiple).
                --label-prefix      Prefix for label predictions.

              Required for downloading issue data:
                --issue-data        Path for issue data file to create (TSV file).

              Required for downloading pull request data:
                --pull-data         Path for pull request data file to create (TSV file).

              Optional arguments:
                --issue-limit       Maximum number of issues to download.
                --pull-limit        Maximum number of pull requests to download.
                --page-size         Number of items per page in GitHub API requests.
                --page-limit        Maximum number of pages to retrieve.
                --retries           Comma-separated retry delays in seconds. Default: 30,30,300,300,3000,3000.
                --token             GitHub access token. Default: Read from GITHUB_TOKEN env var.
                --verbose           Enable verbose output.
            """);

        Environment.Exit(1);
    }

    public static Args? Parse(string[] args)
    {
        Args config = new()
        {
            Retries = [30, 30, 300, 300, 3000, 3000]
        };

        Queue<string> arguments = new(args);
        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (argument)
            {
                case "--token":
                    string? gitHubToken = ArgUtils.Dequeue(arguments);

                    if (gitHubToken is null)
                    {
                        ShowUsage("Argument '--token' has an empty value.");
                        return null;
                    }

                    config.GithubToken = gitHubToken;
                    break;

                case "--repo":
                    string? orgRepos = ArgUtils.Dequeue(arguments);

                    if (orgRepos is null)
                    {
                        ShowUsage("Argument '--repo' has an empty value.");
                        return null;
                    }

                    foreach (var orgRepo in orgRepos.Split(',').Select(r => r.Trim()))
                    {
                        if (!orgRepo.Contains('/'))
                        {
                            ShowUsage($"Argument '--repo' is not in the format of '{{org}}/{{repo}}': {orgRepo}");
                            return null;
                        }

                        string[] parts = orgRepo.Split('/');

                        if (config.Org is not null && config.Org != parts[0])
                        {
                            ShowUsage("All '--repo' values must be from the same org.");
                            return null;
                        }

                        config.Org ??= parts[0];
                        config.Repos ??= [];
                        config.Repos.Add(parts[1]);
                    }
                    break;

                case "--issue-data":
                    config.IssueDataPath = ArgUtils.DequeuePath(arguments);

                    if (config.IssueDataPath is null)
                    {
                        ShowUsage("Argument '--issue-data' has an empty value.");
                        return null;
                    }
                    break;

                case "--issue-limit":
                    config.IssueLimit = ArgUtils.DequeueInt(arguments);

                    if (config.IssueLimit is null)
                    {
                        ShowUsage("Argument '--issue-limit' has an empty or invalid value.");
                        return null;
                    }
                    break;

                case "--pull-data":
                    config.PullDataPath = ArgUtils.DequeuePath(arguments);

                    if (config.PullDataPath is null)
                    {
                        ShowUsage("Argument '--pull-data' has an empty value.");
                        return null;
                    }
                    break;

                case "--pull-limit":
                    config.PullLimit = ArgUtils.DequeueInt(arguments);

                    if (config.PullLimit is null)
                    {
                        ShowUsage("Argument '--pull-limit' has an empty or invalid value.");
                        return null;
                    }
                    break;

                case "--page-size":
                    config.PageSize = ArgUtils.DequeueInt(arguments);

                    if (config.PageSize is null)
                    {
                        ShowUsage("Argument '--page-size' has an empty or invalid value.");
                        return null;
                    }
                    break;

                case "--page-limit":
                    config.PageLimit = ArgUtils.DequeueInt(arguments);

                    if (config.PageLimit is null)
                    {
                        ShowUsage("Argument '--page-limit' has an empty or invalid value.");
                        return null;
                    }
                    break;

                case "--retries":
                    string? retries = ArgUtils.Dequeue(arguments);

                    if (retries is null)
                    {
                        ShowUsage("Argument '--retries' has an empty value.");
                        return null;
                    }

                    config.Retries = retries.Split(',').Select(r => int.Parse(r)).ToArray();
                    break;

                case "--label-prefix":
                    string? labelPrefix = ArgUtils.Dequeue(arguments);

                    if (labelPrefix is null)
                    {
                        ShowUsage("Argument '--label-prefix' has an empty value.");
                        return null;
                    }

                    config.LabelPredicate = (label) => label.StartsWith(labelPrefix, StringComparison.OrdinalIgnoreCase);
                    break;

                case "--verbose":
                    config.Verbose = true;
                    break;
                default:
                    ShowUsage($"Unrecognized argument: {argument}");
                    return null;
            }
        }

        if (config.Org is null || config.Repos is null || config.LabelPredicate is null ||
            (config.IssueDataPath is null && config.PullDataPath is null))
        {
            ShowUsage();
            return null;
        }

        if (config.GithubToken is null)
        {
            string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

            if (string.IsNullOrEmpty(token))
            {
                ShowUsage("Argument '--token' not specified and environment variable GITHUB_TOKEN is empty.");
                return null;
            }

            config.GithubToken = token;
        }

        return config;
    }
}
