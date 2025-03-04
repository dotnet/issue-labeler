// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

public static class ConfigurationParser
{
    private const string DefaultIssuesFileName = "issues.tsv";
    private const string DefaultPullsFileName = "pulls.tsv";

    public static void ShowUsage(string? message = null)
    {
        Console.WriteLine($"ERROR: Invalid or missing arguments.{(message is null ? "" : " " + message)}");
        Console.WriteLine();

        string executableName = Process.GetCurrentProcess().ProcessName;

        Console.WriteLine("Usage:");
        Console.WriteLine();
        Console.WriteLine($"  {executableName} --repo {{org/repo1}}[,{{org/repo2}},...] --label-prefix {{label-prefix}} [options]");
        Console.WriteLine();
        Console.WriteLine("  Required arguments:");
        Console.WriteLine("      --repo              The GitHub repositories in format org/repo (comma separated for multiple)");
        Console.WriteLine("      --label-prefix      Prefix to filter GitHub labels");
        Console.WriteLine();
        Console.WriteLine("  Optional arguments:");
        Console.WriteLine($"      --issue-data        Path to issue data file. Default: <current folder>/{DefaultIssuesFileName}");
        Console.WriteLine("      --issue-limit       Maximum number of issues to download");
        Console.WriteLine($"      --pull-data         Path to pull request data file. Default: <current folder>/{DefaultPullsFileName}");
        Console.WriteLine("      --pull-limit        Maximum number of pull requests to download");
        Console.WriteLine("      --page-size         Number of items per page in GitHub API requests");
        Console.WriteLine("      --page-limit        Maximum number of pages to retrieve");
        Console.WriteLine("      --retries           Comma-separated retry delays in seconds. Default: 30,30,300,300,3000,3000");
        Console.WriteLine("      --token             GitHub access token. Default: read from GITHUB_TOKEN env var");
        Console.WriteLine("      --verbose           Enable verbose output");

        Environment.Exit(1);
    }

    public static Configuration? Parse(string[] args)
    {
        string? gitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

        Queue<string> arguments = new(args);
        Configuration config = new()
        {
            Repos = [],
            Retries = [30, 30, 300, 300, 3000, 3000]
        };

        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (argument)
            {
                case "--token":
                    gitHubToken = arguments.Dequeue();
                    break;
                case "--repo":
                    string orgRepos = arguments.Dequeue();

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
                        config.Repos.Add(parts[1]);
                    }

                    break;
                case "--issue-data":
                    config.IssuesPath = arguments.Dequeue();
                    break;
                case "--issue-limit":
                    config.IssueLimit = int.Parse(arguments.Dequeue());
                    break;
                case "--pull-data":
                    config.PullsPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(config.PullsPath))
                    {
                        ShowUsage("Argument '--pull-data' has an empty value.");
                        return null;
                    }

                    break;
                case "--pull-limit":
                    config.PullLimit = int.Parse(arguments.Dequeue());
                    break;
                case "--page-size":
                    config.PageSize = int.Parse(arguments.Dequeue());
                    break;
                case "--page-limit":
                    config.PageLimit = int.Parse(arguments.Dequeue());
                    break;
                case "--retries":
                    config.Retries = arguments.Dequeue().Split(',').Select(r => int.Parse(r)).ToArray();
                    break;
                case "--label-prefix":
                    string labelPrefix = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(labelPrefix))
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

        if (config.Org is null || config.Repos.Count == 0 || gitHubToken is null || config.LabelPredicate is null)
        {
            ShowUsage();
            return null;
        }

        config.GithubToken = gitHubToken;
        config.IssuesPath = ResolvePath(config.IssuesPath, defaultFileName: DefaultIssuesFileName);
        config.PullsPath = ResolvePath(config.PullsPath, defaultFileName: DefaultPullsFileName);

        return config;

        static string ResolvePath(string? path, string defaultFileName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Path.Combine(Environment.CurrentDirectory, defaultFileName);
            }

            if (!Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            return path;
        }
    }
}
