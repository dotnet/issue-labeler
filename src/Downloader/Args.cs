// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public static class Args
{
    public static void ShowUsage(string? message = null)
    {
        Console.WriteLine($"Invalid or missing arguments.{(message is null ? "" : " " + message)}");
        Console.WriteLine("  --token {github_token}");
        Console.WriteLine("  --repo {org/repo1}[,{org/repo2},...]");
        Console.WriteLine("  --label-prefix {label-prefix}");
        Console.WriteLine("  [--issue-data {path/to/issues.tsv}]");
        Console.WriteLine("  [--issue-limit {rows}]");
        Console.WriteLine("  [--pull-data {path/to/pulls.tsv}]");
        Console.WriteLine("  [--pull-limit {rows}]");
        Console.WriteLine("  [--page-size {size}]");
        Console.WriteLine("  [--page-limit {pages}]");
        Console.WriteLine("  [--retries {comma-separated-retries-in-seconds}]");
        Console.WriteLine("  [--verbose]");

        Environment.Exit(1);
    }

    public static (
        string org,
        string[] repos,
        string githubToken,
        string? issuesPath,
        int? issueLimit,
        string? pullsPath,
        int? pullLimit,
        int? pageSize,
        int? pageLimit,
        int[] retries,
        Predicate<string> labelPredicate,
        bool verbose
    )?
    Parse(string[] args)
    {
        Queue<string> arguments = new(args);
        string? org = null;
        List<string>? repos = null;
        string? githubToken = null;
        string? issuesPath = null;
        int? issueLimit = null;
        string? pullsPath = null;
        int? pullLimit = null;
        int? pageSize = null;
        int? pageLimit = null;
        int[] retries = [30, 30, 300, 300, 3000, 3000];
        Predicate<string>? labelPredicate = null;
        bool verbose = false;

        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (argument)
            {
                case "--token":
                    githubToken = arguments.Dequeue();
                    break;
                case "--repo":
                    string orgRepos = arguments.Dequeue();

                    foreach (var orgRepo in orgRepos.Split(',').Select(r => r.Trim()))
                    {
                        if (!orgRepo.Contains('/'))
                        {
                            ShowUsage($$"""Argument '--repo' is not in the format of '{org}/{repo}': {{orgRepo}}""");
                            return null;
                        }

                        string[] parts = orgRepo.Split('/');

                        if (org is not null && org != parts[0])
                        {
                            ShowUsage("All '--repo' values must be from the same org.");
                            return null;
                        }

                        org ??= parts[0];
                        repos ??= new();
                        repos.Add(parts[1]);
                    }

                    break;
                case "--issue-data":
                    issuesPath = arguments.Dequeue();
                    break;
                case "--issue-limit":
                    issueLimit = int.Parse(arguments.Dequeue());
                    break;
                case "--pull-data":
                    pullsPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(pullsPath))
                    {
                        ShowUsage("Argument '--pull-data' has an empty value.");
                        return null;
                    }

                    break;
                case "--pull-limit":
                    pullLimit = int.Parse(arguments.Dequeue());
                    break;
                case "--page-size":
                    pageSize = int.Parse(arguments.Dequeue());
                    break;
                case "--page-limit":
                    pageLimit = int.Parse(arguments.Dequeue());
                    break;
                case "--retries":
                    retries = arguments.Dequeue().Split(',').Select(r => int.Parse(r)).ToArray();
                    break;
                case "--label-prefix":
                    string labelPrefix = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(labelPrefix))
                    {
                        ShowUsage("Argument '--label-prefix' has an empty value.");
                        return null;
                    }

                    labelPredicate = (label) => label.StartsWith(labelPrefix, StringComparison.OrdinalIgnoreCase);
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                default:
                    ShowUsage($"Unrecognized argument: {argument}");
                    return null;
            }
        }

        if (org is null || repos is null || githubToken is null || labelPredicate is null ||
            (issuesPath is null && pullsPath is null))
        {
            ShowUsage();
            return null;
        }

        return (
            org,
            repos.ToArray(),
            githubToken,
            issuesPath,
            issueLimit,
            pullsPath,
            pullLimit,
            pageSize,
            pageLimit,
            retries,
            labelPredicate,
            verbose
        );
    }
}
