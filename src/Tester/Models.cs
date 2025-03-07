// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public class Issue
{
    public ulong Number { get; set; }
    public string? Label { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }

    // The Area and Description properties allow loading
    // models from the issue-labeler implementation
    public string? Area { get => Label; }
    public string? Description { get => Body; }

    public Issue() { }

    public Issue(GitHubClient.Issue issue, Predicate<string> labelPredicate)
    {
        Number = issue.Number;
        Title = issue.Title;
        Body = issue.Body;
        Label = issue.Labels.HasNextPage ?
            (string?) null :
            issue.LabelNames?.SingleOrDefault(l => labelPredicate(l));
    }
}

public class PullRequest : Issue
{
    public string? FileNames { get; set; }
    public string? FolderNames { get; set; }

    public PullRequest() { }

    public PullRequest(GitHubClient.PullRequest pull, Predicate<string> labelPredicate) : base(pull, labelPredicate)
    {
        FileNames = string.Join(' ', pull.FileNames);
        FolderNames = string.Join(' ', pull.FolderNames);
    }
}

public class LabelPrediction
{
    public string? PredictedLabel { get; set; }
    public float[]? Score { get; set; }
}
