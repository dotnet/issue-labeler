// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Actions.Core.Services;
using IssueLabeler.Evaluation;
using System.Globalization;

public struct Args
{
    public readonly string GitHubToken => Environment.GetEnvironmentVariable("GITHUB_TOKEN")!;
    public string Org { get; set; }
    public List<string> Repos { get; set; }
    public float Threshold { get; set; }
    public Predicate<string> LabelPredicate { get; set; }
    public string[]? ExcludedAuthors { get; set; }
    public string[]? ExcludedLabels { get; set; }
    public string? IssuesModelPath { get; set; }
    public bool TestDiscussions { get; set; }
    public int? IssuesLimit { get; set; }
    public int? DiscussionsLimit { get; set; }
    public string? PullsModelPath { get; set; }
    public int? PullsLimit { get; set; }
    public int? PageSize { get; set; }
    public int? PageLimit { get; set; }
    public int[] Retries { get; set; }
    public bool Verbose { get; set; }
    public string? EvaluationPath { get; set; }
    public double[]? TargetPrecisions { get; set; }
    public string? ReportDirectory { get; set; }
    public string? LabelPrefix { get; set; }

    static void ShowUsage(string? message, ICoreService action)
    {
        action.WriteNotice($$"""
            ERROR: Invalid or missing arguments.{{(message is null ? "" : " " + message)}}

            Historical recheck environment variables:
              GITHUB_TOKEN            GitHub token to be used for API calls.

            Required arguments:
              --repo                  The GitHub repositories in format org/repo (comma separated for multiple).
              --label-prefix          Prefix for label predictions. Must end with a character other than a letter or number.

            Required for testing the issues model:
              --issues-model          Path to existing issue prediction model file (ZIP file).
              --discussions           Also evaluate discussions using the issues prediction model.

            Required for testing the pull requests model:
              --pulls-model           Path to existing pull request prediction model file (ZIP file).

            Optional arguments:
              --excluded-authors      Comma-separated list of authors to exclude.
              --excluded-labels       Comma-separated list of labels to exclude.
              --threshold             Minimum prediction confidence threshold. Range (0,1].
                                      Defaults to: 0.4.
              --issues-limit          Maximum number of issues to download. Defaults to: No limit.
              --discussions-limit     Maximum number of discussions to download. Defaults to: No limit.
              --pulls-limit           Maximum number of pull requests to download. Defaults to: No limit.
              --page-size             Number of items per page in GitHub API requests.
                                      Defaults to: 100 for issues, 25 for pull requests.
              --page-limit            Maximum number of pages to retrieve.
                                      Defaults to: 1000 for issues, 4000 for pull requests.
              --retries               Comma-separated retry delays in seconds.
                                      Defaults to: 30,30,300,300,3000,3000.
              --verbose               Enable verbose output.

            Offline held-out evaluation (no GitHub token or repository required):
              --evaluation            Saved .evaluation.json from the same training run as the model.
              --issues-model          Issue model ZIP, or use --pulls-model for a pull request model.
              --label-prefix          Prefix used to select applicable model labels.
              --target-precisions     Comma-separated target precisions in [0,1].
                                      Defaults to 0.80,0.90,0.95. Selects maximum-coverage cutoffs.
                                      The --threshold value remains a reference (default 0.4).
              --report                Output directory for Markdown, JSON and CSV reports.
                                      Defaults to a reports directory beside the saved scores.
                                      Historical download/filter options cannot be combined with this mode.
            """);

        Environment.Exit(1);
    }

    public static Args? Parse(string[] args, ICoreService action)
    {
        Queue<string> arguments = new(args);
        ArgUtils argUtils = new(action, ShowUsage, arguments);

        Args argsData = new()
        {
            Threshold = 0.4f,
            Retries = [30, 30, 300, 300, 3000, 3000]
        };

        bool historicalOptions = false;
        bool evaluationOptions = false;
        string? thresholdInput = null;

        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();
            historicalOptions |= argument is "--repo" or "--excluded-authors" or "--excluded-labels" or
                "--discussions" or "--issues-limit" or "--discussions-limit" or "--pulls-limit" or
                "--page-size" or "--page-limit" or "--retries" or "--verbose";

            switch (argument)
            {
                case "--repo":
                    if (!argUtils.TryGetRepoList("--repo", out string? org, out List<string>? repos))
                    {
                        return null;
                    }
                    argsData.Org = org;
                    argsData.Repos = repos;
                    break;

                case "--label-prefix":
                    if (!argUtils.TryGetLabelPrefix("--label-prefix", out string? labelPrefix, out Func<string, bool>? labelPredicate))
                    {
                        return null;
                    }
                    argsData.LabelPrefix = labelPrefix;
                    argsData.LabelPredicate = new(labelPredicate);
                    break;

                case "--evaluation":
                    if (!argUtils.TryGetPath("--evaluation", out string? evaluationPath))
                    {
                        ShowUsage("Input '--evaluation' requires a saved-scores path.", action);
                        return null;
                    }
                    argsData.EvaluationPath = evaluationPath;
                    break;

                case "--target-precisions":
                    evaluationOptions = true;
                    if (!argUtils.TryGetString("--target-precisions", out string? targets))
                    {
                        ShowUsage("Input '--target-precisions' requires a comma-separated list.", action);
                        return null;
                    }
                    try
                    {
                        argsData.TargetPrecisions = ThresholdEvaluation.ParsePrecisionTargets(targets);
                    }
                    catch (ArgumentException ex)
                    {
                        ShowUsage(ex.Message, action);
                        return null;
                    }
                    break;

                case "--report":
                    evaluationOptions = true;
                    if (!argUtils.TryGetPath("--report", out string? reportDirectory))
                    {
                        ShowUsage("Input '--report' requires an output directory.", action);
                        return null;
                    }
                    argsData.ReportDirectory = reportDirectory;
                    break;

                case "--excluded-authors":
                    if (!argUtils.TryGetStringArray("--excluded-authors", out string[]? excludedAuthors))
                    {
                        return null;
                    }
                    argsData.ExcludedAuthors = excludedAuthors;
                    break;

                case "--excluded-labels":
                    if (!argUtils.TryGetStringArray("--excluded-labels", out string[]? excludedLabels))
                    {
                        return null;
                    }
                    argsData.ExcludedLabels = LabelUtils.NormalizeLabels(excludedLabels);
                    break;

                case "--threshold":
                    if (!argUtils.TryGetString("--threshold", out thresholdInput))
                    {
                        ShowUsage("Input '--threshold' requires a decimal value.", action);
                        return null;
                    }
                    break;

                case "--issues-model":
                    if (!argUtils.TryGetPath("--issues-model", out string? IssuesModelPath))
                    {
                        return null;
                    }
                    argsData.IssuesModelPath = IssuesModelPath;
                    break;

                case "--discussions":
                    argsData.TestDiscussions = true;
                    break;

                case "--issues-limit":
                    if (!argUtils.TryGetInt("--issues-limit", out int? IssuesLimit))
                    {
                        return null;
                    }
                    argsData.IssuesLimit = IssuesLimit;
                    break;

                case "--discussions-limit":
                    if (!argUtils.TryGetInt("--discussions-limit", out int? discussionsLimit))
                    {
                        return null;
                    }
                    argsData.DiscussionsLimit = discussionsLimit;
                    break;

                case "--pulls-model":
                    if (!argUtils.TryGetPath("--pulls-model", out string? PullsModelPath))
                    {
                        return null;
                    }
                    argsData.PullsModelPath = PullsModelPath;
                    break;

                case "--pulls-limit":
                    if (!argUtils.TryGetInt("--pulls-limit", out int? PullsLimit))
                    {
                        return null;
                    }
                    argsData.PullsLimit = PullsLimit;
                    break;

                case "--page-size":
                    if (!argUtils.TryGetInt("--page-size", out int? pageSize))
                    {
                        return null;
                    }
                    argsData.PageSize = pageSize;
                    break;

                case "--page-limit":
                    if (!argUtils.TryGetInt("--page-limit", out int? pageLimit))
                    {
                        return null;
                    }
                    argsData.PageLimit = pageLimit;
                    break;

                case "--retries":
                    if (!argUtils.TryGetIntArray("--retries", out int[]? retries))
                    {
                        return null;
                    }
                    argsData.Retries = retries;
                    break;

                case "--verbose":
                    argsData.Verbose = true;
                    break;

                default:
                    ShowUsage($"Unrecognized argument: {argument}", action);
                    return null;
            }
        }

        if (argsData.EvaluationPath is not null)
        {
            if (!float.TryParse(thresholdInput ?? "0.4", NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float reference) ||
                !float.IsFinite(reference) || reference < 0 || reference > 1)
            {
                ShowUsage("Input '--threshold' requires one invariant decimal value between 0 and 1.", action);
                return null;
            }
            argsData.Threshold = reference;

            if (historicalOptions || argsData.LabelPredicate is null ||
                (argsData.IssuesModelPath is null) == (argsData.PullsModelPath is null) ||
                !float.IsFinite(argsData.Threshold) || argsData.Threshold < 0 || argsData.Threshold > 1)
            {
                ShowUsage("Offline evaluation requires a label prefix and exactly one model; historical download/filter options are not allowed.", action);
                return null;
            }

            argsData.TargetPrecisions ??= [0.80, 0.90, 0.95];
            argsData.ReportDirectory ??= Path.Combine(Path.GetDirectoryName(argsData.EvaluationPath)!, "evaluation-report");
            return argsData;
        }

        if (evaluationOptions)
        {
            ShowUsage("Inputs '--target-precisions' and '--report' require '--evaluation'.", action);
            return null;
        }

        if (thresholdInput is not null)
        {
            if (!argUtils.TryParseFloat("--threshold", thresholdInput, out float? threshold))
            {
                return null;
            }
            argsData.Threshold = threshold.Value;
        }

        if (string.IsNullOrEmpty(argsData.GitHubToken))
        {
            ShowUsage("Environment variable GITHUB_TOKEN is empty.", action);
            return null;
        }

        if (argsData.Org is null || argsData.Repos is null || argsData.Repos.Count == 0 || argsData.LabelPredicate is null ||
            (argsData.IssuesModelPath is null && argsData.PullsModelPath is null))
        {
            ShowUsage(null, action);
            return null;
        }

        if (argsData.TestDiscussions && argsData.IssuesModelPath is null)
        {
            ShowUsage("Argument '--issues-model' is required when '--discussions' is provided.", action);
            return null;
        }

        return argsData;
    }
}
