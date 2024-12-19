// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// Various models used to deserialize GraphQL responses from GitHub
namespace ModelCreator.Downloader;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
public class GitHubListPage<T>
{
    public bool IsError { get; set; }
    public Data<T> Issues { get; set; }
}

public class Data<T>
{
    public Repository<T> Repository { get; set; }
}

public class Repository<T>
{
    public string Name { get; set; }
    public Items<T> Issues { get; set; }
}

public class Items<T>
{
    public List<T> Nodes { get; set; }
    public PageInfo PageInfo { get; set; }
    public long TotalCount { get; set; }
}

public class IssuesNode
{
    public long Number { get; set; }
    public string Title { get; set; }
    public string BodyText { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Actor Author { get; set; }
    public Labels Labels { get; set; }
}

public class PullRequestsNode : IssuesNode
{
    public Items<ChangedPrFiles> Files { get; set; }
}

public class Actor
{
    public string Login { get; set; }
}

public class ChangedPrFiles
{
    public string Path { get; set; }
}

public class Labels
{
    public List<LabelsNode> Nodes { get; set; }
    public long TotalCount { get; set; }
}

public class LabelsNode
{
    public string Name { get; set; }
}

public class PageInfo
{
    public bool HasNextPage { get; set; }
    public string EndCursor { get; set; }
}
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
