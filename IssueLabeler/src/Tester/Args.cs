// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Actions.Core.Services;

public struct Args
{
    public float Threshold { get; set; }
    public string[]? ExcludedLabels { get; set; }
    public string? IssuesModelPath { get; set; }
    public bool TestDiscussions { get; set; }
    public string? PullsModelPath { get; set; }
    public bool Verbose { get; set; }

    static void ShowUsage(string? message, ICoreService action)
    {
        action.WriteNotice($$"""
            ERROR: Invalid or missing arguments.{{(message is null ? "" : " " + message)}}

            Supply at least one model:
              --issues-model          Path to the issue prediction model (ZIP file).
              --pulls-model           Path to the pull request prediction model (ZIP file).

            Each model requires its saved held-out dataset beside it:
              example-model.zip       Model file.
              example-model.test-data.bin
                                      The exact test split saved during training.

            Optional arguments:
              --discussions           Treat --issues-model as a discussion model instead.
              --excluded-labels       Comma-separated labels to report if detected.
                                      Does not change the held-out dataset.
              --threshold             Minimum prediction confidence threshold. Range [0,1].
                                      Defaults to: 0.4.
              --verbose               Log predictions for individual held-out rows.
            """);

        Environment.Exit(1);
    }

    public static Args? Parse(string[] args, ICoreService action)
    {
        Queue<string> arguments = new(args);
        ArgUtils argUtils = new(action, ShowUsage, arguments);
        Args argsData = new()
        {
            Threshold = 0.4f
        };

        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (argument)
            {
                case "--issues-model":
                    if (!argUtils.TryGetPath(argument, out string? issuesModelPath))
                    {
                        ShowUsage($"Input '{argument}' requires a path.", action);
                        return null;
                    }
                    argsData.IssuesModelPath = issuesModelPath;
                    break;

                case "--pulls-model":
                    if (!argUtils.TryGetPath(argument, out string? pullsModelPath))
                    {
                        ShowUsage($"Input '{argument}' requires a path.", action);
                        return null;
                    }
                    argsData.PullsModelPath = pullsModelPath;
                    break;

                case "--discussions":
                    argsData.TestDiscussions = true;
                    break;

                case "--excluded-labels":
                    if (!argUtils.TryGetStringArray(argument, out string[]? excludedLabels))
                    {
                        ShowUsage($"Input '{argument}' requires a label list.", action);
                        return null;
                    }
                    argsData.ExcludedLabels = LabelUtils.NormalizeLabels(excludedLabels);
                    break;

                case "--threshold":
                    if (!argUtils.TryGetFloat(argument, out float? threshold))
                    {
                        return null;
                    }
                    if (!float.IsFinite(threshold.Value) || threshold.Value is < 0 or > 1)
                    {
                        ShowUsage("The threshold must be between 0 and 1, inclusive.", action);
                        return null;
                    }
                    argsData.Threshold = threshold.Value;
                    break;

                case "--verbose":
                    argsData.Verbose = true;
                    break;

                default:
                    ShowUsage($"Unrecognized argument: {argument}", action);
                    return null;
            }
        }

        if (argsData.IssuesModelPath is null && argsData.PullsModelPath is null)
        {
            ShowUsage("At least one model path is required.", action);
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
