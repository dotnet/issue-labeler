// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public struct Configuration
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
}
