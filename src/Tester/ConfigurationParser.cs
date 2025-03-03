// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public static class ConfigurationParser
{
    public static void ShowUsage(string? message = null)
    {
        Console.WriteLine($"Invalid or missing arguments.{(message is null ? "" : " " + message)}");
        Console.WriteLine("  --label-prefix {label-prefix}");
        Console.WriteLine("  [--threshold {threshold}]");
        Console.WriteLine("  [--repo {org/repo1}[,{org/repo2},...]]");
        Console.WriteLine("  [--issue-data {path/to/issue-data.tsv}]");
        Console.WriteLine("  [--issue-model {path/to/issue-model.zip}]");
        Console.WriteLine("  [--issue-limit {issues}]");
        Console.WriteLine("  [--pull-data {path/to/pull-data.tsv}]");
        Console.WriteLine("  [--pull-model {path/to/pull-model.zip}]");
        Console.WriteLine("  [--pull-limit {pulls}]");
        Console.WriteLine("  [--token {github_token}]. Default: read from GITHUB_TOKEN env var");

        Environment.Exit(1);
    }

    public static Configuration? Parse(string[] args)
    {
        Queue<string> arguments = new(args);
        Configuration config = new()
        {
            Repos = [],
            GithubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
        };

        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (argument)
            {
                case "--token":
                    config.GithubToken = arguments.Dequeue();
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
                    config.IssueDataPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(config.IssueDataPath))
                    {
                        ShowUsage("Argument '--issue-data' has an empty value.");
                        return null;
                    }

                    break;
                case "--issue-model":
                    config.IssueModelPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(config.IssueModelPath))
                    {
                        ShowUsage("Argument '--issue-model' has an empty value.");
                        return null;
                    }

                    break;
                case "--issue-limit":
                    config.IssueLimit = int.Parse(arguments.Dequeue());
                    break;
                case "--pull-data":
                    config.PullDataPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(config.PullDataPath))
                    {
                        ShowUsage("Argument '--pull-data' has an empty value.");
                        return null;
                    }

                    break;
                case "--pull-model":
                    config.PullModelPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(config.PullModelPath))
                    {
                        ShowUsage("Argument '--pull-model' has an empty value.");
                        return null;
                    }

                    break;
                case "--pull-limit":
                    config.PullLimit = int.Parse(arguments.Dequeue());
                    break;
                case "--label-prefix":
                    string labelPrefix = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(labelPrefix))
                    {
                        ShowUsage("Argument '--label-prefix' has an empty value.");
                        return null;
                    }

                    config.LabelPredicate = label => label.StartsWith(labelPrefix, StringComparison.OrdinalIgnoreCase);
                    break;
                case "--threshold":
                    config.Threshold = float.Parse(arguments.Dequeue());
                    break;
                default:
                    ShowUsage($"Unrecognized argument: {argument}");
                    return null;
            }
        }

        if (
            config.LabelPredicate is null ||
            (
                config.IssueDataPath is null && config.PullDataPath is null &&
                (config.Org is null || config.Repos.Count == 0 || config.GithubToken is null)
            ) ||
            (config.IssueModelPath is null && config.PullModelPath is null)
        )
        {
            ShowUsage();
            return null;
        }

        return config;
    }
}
