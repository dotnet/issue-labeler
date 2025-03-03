// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public struct Configuration
{
    public string Org { get; set; }
    public string Repo { get; set; }
    public string GithubToken { get; set; }
    public string? IssueModelPath { get; set; }
    public List<ulong>? IssueNumbers { get; set; }
    public string? PullModelPath { get; set; }
    public List<ulong>? PullNumbers { get; set; }
    public float Threshold { get; set; }
    public Func<string, bool> LabelPredicate { get; set; }
    public string? DefaultLabel { get; set; }
    public bool Test { get; set; }
}
