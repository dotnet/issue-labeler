// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public struct Configuration
{
    public string Org { get; set; }
    public List<string> Repos { get; set; }
    public string GithubToken { get; set; }
    public string? IssuesPath { get; set; }
    public int? IssueLimit { get; set; }
    public string? PullsPath { get; set; }
    public int? PullLimit { get; set; }
    public int? PageSize { get; set; }
    public int? PageLimit { get; set; }
    public int[] Retries { get; set; }
    public Predicate<string> LabelPredicate { get; set; }
    public bool Verbose { get; set; }
}
