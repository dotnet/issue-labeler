// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public static class Args
{
    public static void ShowUsage(string? message = null)
    {
        Console.WriteLine($"Invalid or missing arguments.{(message is null ? "" : " " + message)}");
        Console.WriteLine("  --label-prefix {label-prefix}");
        Console.WriteLine("  [--threshold {threshold}]");
        Console.WriteLine("  [--token {github_token} --repo {org/repo1}[,{org/repo2},...]]");
        Console.WriteLine("  [--issue-data {path/to/issue-data.tsv}");
        Console.WriteLine("  [--issue-model {path/to/issue-model.zip}]");
        Console.WriteLine("  [--issue-limit {issues}]");
        Console.WriteLine("  [--pull-data {path/to/pull-data.tsv}");
        Console.WriteLine("  [--pull-model {path/to/pull-model.zip}]");
        Console.WriteLine("  [--pull-limit {pulls}]");

        Environment.Exit(1);
    }

    public static (
        string? org,
        string[]? repos,
        string? githubToken,
        string? issueDataPath,
        string? issueModelPath,
        int? issueLimit,
        string? pullDataPath,
        string? pullModelPath,
        int? pullLimit,
        float? threshold,
        Predicate<string> labelPredicate
    )?
    Parse(string[] args)
    {
        Queue<string> arguments = new(args);
        string? org = null;
        List<string>? repos = null;
        string? githubToken = null;
        string? issueDataPath = null;
        string? issueModelPath = null;
        int? issueLimit = null;
        string? pullDataPath = null;
        string? pullModelPath = null;
        int? pullLimit = null;
        float? threshold = null;
        Predicate<string>? labelPredicate = null;

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
                    issueDataPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(issueDataPath))
                    {
                        ShowUsage("Argument '--issue-data' has an empty value.");
                        return null;
                    }

                    break;
                case "--issue-model":
                    issueModelPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(issueModelPath))
                    {
                        ShowUsage("Argument '--issue-model' has an empty value.");
                        return null;
                    }

                    break;
                case "--issue-limit":
                    issueLimit = int.Parse(arguments.Dequeue());
                    break;
                case "--pull-data":
                    pullDataPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(pullDataPath))
                    {
                        ShowUsage("Argument '--pull-data' has an empty value.");
                        return null;
                    }

                    break;
                case "--pull-model":
                    pullModelPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(pullModelPath))
                    {
                        ShowUsage("Argument '--pull-model' has an empty value.");
                        return null;
                    }

                    break;
                case "--pull-limit":
                    pullLimit = int.Parse(arguments.Dequeue());
                    break;
                case "--label-prefix":
                    string labelPrefix = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(labelPrefix))
                    {
                        ShowUsage("Argument '--label-prefix' has an empty value.");
                        return null;
                    }

                    labelPredicate = label => label.StartsWith(labelPrefix, StringComparison.OrdinalIgnoreCase);
                    break;
                case "--threshold":
                    threshold = float.Parse(arguments.Dequeue());
                    break;
                default:
                    ShowUsage($"Unrecognized argument: {argument}");
                    return null;
            }
        }

        if (
            labelPredicate is null ||
            (
                issueDataPath is null && pullDataPath is null &&
                (org is null || repos is null || githubToken is null)
            ) ||
            (issueModelPath is null && pullModelPath is null)
        )
        {
            ShowUsage();
            return null;
        }

        return (
            org,
            repos?.ToArray(),
            githubToken,
            issueDataPath,
            issueModelPath,
            issueLimit,
            pullDataPath,
            pullModelPath,
            pullLimit,
            threshold,
            (Predicate<string>)labelPredicate
        );
    }
}
