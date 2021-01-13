// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CreateMikLabelModel.DL.Common;
using CreateMikLabelModel.DL.GraphQL;
using CreateMikLabelModel.Models;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CreateMikLabelModel.DL
{
    class GraphQLDownloadHelper
    {
        public const int MaxRetryCount = 25;
        private const int MaxFileChangesPerPR = 100;
        private const string DeletedUser = "ghost";

        public static async Task<bool> DownloadFastUsingGraphQLAsync(
            Dictionary<(DateTimeOffset, long, string), string> outputLinesExcludingHeader,
            (string owner, string repo)[] repoCombo,
            StreamWriter outputWriter)
        {
            try
            {
                foreach ((string owner, string repo) repo in repoCombo)
                {
                    using (var client = CommonHelper.CreateGraphQLClient())
                    {
                        Trace.WriteLine($"Downloading Issue records from {repo.owner}/{repo.repo}.");
                        if (!await ProcessGitHubIssueData(
                            client, repo.owner, repo.repo, IssueType.Issue, outputLinesExcludingHeader, GetGitHubIssuePage<IssuesNode>))
                        {
                            return false;
                        }
                        Trace.WriteLine($"Downloading PR records from {repo.owner}/{repo.repo}.");
                        if (!await ProcessGitHubIssueData(
                            client, repo.owner, repo.repo, IssueType.PullRequest, outputLinesExcludingHeader, GetGitHubIssuePage<PullRequestsNode>))
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"{nameof(DownloadFastUsingGraphQLAsync)}:{ex.Message}");
                return false;
            }
            finally
            {
                CommonHelper.action(outputLinesExcludingHeader.Values.ToList(), outputWriter);
            }
        }

        public static async Task<bool> ProcessGitHubIssueData<T>(
            GraphQLHttpClient ghGraphQL, string owner, string repo, IssueType issueType, Dictionary<(DateTimeOffset, long, string), string> outputLines,
            Func<GraphQLHttpClient, string, string, IssueType, string, Task<GitHubListPage<T>>> getPage) where T : IssuesNode
        {
            Trace.WriteLine($"Getting all '{issueType}' items for {owner}/{repo}...");
            int backToBackFailureCount = 0;
            var hasNextPage = true;
            string afterID = null;
            var totalProcessed = 0;
            do
            {
                try
                {
                    var issuePage = await getPage(ghGraphQL, owner, repo, issueType, afterID);

                    if (issuePage.IsError)
                    {
                        Trace.WriteLine("Error encountered in GraphQL query. Stopping.");
                        return false;
                    }

                    var issuesOfInterest =
                        issuePage.Issues.Repository.Issues.Nodes
                            .Where(i => IsIssueOfInterest(i))
                            .ToList();

                    var uninterestingIssuesWithTooManyLabels =
                        issuePage.Issues.Repository.Issues.Nodes
                            .Except(issuesOfInterest)
                            .Where(i => i.Labels.TotalCount > 10);

                    if (uninterestingIssuesWithTooManyLabels.Any())
                    {
                        // The GraphQL query gets at most 10 labels per issue. So if an issue has more than 10 labels,
                        // but none of the first 10 are an 'area-' label, then it's possible that one of the unseen
                        // labels is an 'area-' label and we don't know about it. So we warn.
                        foreach (var issue in uninterestingIssuesWithTooManyLabels)
                        {
                            Trace.WriteLine(
                                $"\tWARNING: Issue {owner}/{repo}#{issue.Number} has more than 10 labels " +
                                $"and the first 10 aren't 'area-' labels so it is ignored.");
                        }
                    }

                    if (issueType == IssueType.PullRequest)
                    {
                        var prsWithTooManyFileChanges =
                            issuePage.Issues.Repository.Issues.Nodes
                                .OfType<PullRequestsNode>()
                                .Where(i => i.Files != null && i.Files.TotalCount > MaxFileChangesPerPR);

                        if (prsWithTooManyFileChanges.Any())
                        {
                            // The GraphQL query gets at most N file changes per pr. So if a pr has more than N files changed,
                            // then it's possible that we don't know about it. So we warn.
                            foreach (var issue in prsWithTooManyFileChanges)
                            {
                                Trace.WriteLine(
                                    $"\tWARNING: PR {owner}/{repo}#{issue.Number} has more than {MaxFileChangesPerPR} files changed, ({issue.Files.TotalCount} total)" +
                                    $"and the first {MaxFileChangesPerPR} are only used for training its area.");
                            }
                        }
                    }

                    totalProcessed += issuePage.Issues.Repository.Issues.Nodes.Count;
                    Trace.WriteLine(
                        $"Processing {totalProcessed}/{issuePage.Issues.Repository.Issues.TotalCount}. " +
                        $"Writing {issuesOfInterest.Count} items of interest to output TSV file...");

                    foreach (var issue in issuesOfInterest)
                    {
                        WriteCsvIssue(outputLines, issue, issueType, repo);
                    }
                    hasNextPage = issuePage.Issues.Repository.Issues.PageInfo.HasNextPage;
                    afterID = issuePage.Issues.Repository.Issues.PageInfo.EndCursor;
                    backToBackFailureCount = 0; // reset for next round
                }
                catch (Exception cx)
                {
                    Trace.WriteLine(cx.Message);
                    Trace.WriteLine(string.Join(Environment.NewLine, cx.StackTrace));
                    if (backToBackFailureCount < MaxRetryCount)
                    {
                        backToBackFailureCount++;
                        await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                    else
                    {
                        Trace.WriteLine($"Retried {MaxRetryCount} consecutive times, skip and move on");
                        hasNextPage = false;
                        // TODO later: investigate different reasons for which this might happen
                    }
                }
            }
            while (hasNextPage);

            return true;
        }

        /// <summary>
        /// Returns 'true' if the issue has at least one 'area-*' label, meaning it can be
        /// used to create training data.
        /// </summary>
        /// <param name="issue"></param>
        /// <returns></returns>
        private static bool IsIssueOfInterest(IssuesNode issue)
        {
            return issue.Labels.Nodes.Any(l => l.Name.StartsWith("area-", StringComparison.OrdinalIgnoreCase));
        }

        private static void WriteCsvIssue(Dictionary<(DateTimeOffset, long, string), string> outputLines, IssuesNode issue, IssueType issueType
            // TODO: lookup HtmlUrl for transferred files, may be different than repo
            , string repo)
        {
            var author = issue.Author != null ? issue.Author.Login : DeletedUser;
            var area = issue.Labels.Nodes.First(l => l.Name.StartsWith("area-", StringComparison.OrdinalIgnoreCase)).Name;
            var body = issue.BodyText.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Replace('"', '`');
            var createdAt = issue.CreatedAt.UtcDateTime.ToFileTimeUtc();
            if (issueType == IssueType.Issue)
            {
                outputLines.Add(
                    (issue.CreatedAt, issue.Number, repo),
                    $"{createdAt},{repo},{issue.Number}\t{issue.Number}\t{area}\t{issue.Title}\t{body}\t{author}\t0\t");
            }
            else if (issueType == IssueType.PullRequest && issue is PullRequestsNode pullRequest)
            {
                var filePaths = string.Empty;
                if (pullRequest.Files != null && pullRequest.Files.Nodes.Count > 0)
                {
                    filePaths = string.Join(";", pullRequest.Files.Nodes.Select(x => x.Path));
                }

                outputLines.Add(
                    (issue.CreatedAt, issue.Number, repo),
                    $"{createdAt},{repo},{issue.Number}\t{pullRequest.Number}\t{area}\t{pullRequest.Title}\t{body}\t{author}\t1\t{filePaths}");
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(issueType));
            }
        }

        public static async Task<GitHubListPage<T>> GetGitHubIssuePage<T>(GraphQLHttpClient ghGraphQL, string owner, string repo, IssueType issueType, string afterID)
        {
            var prSpecific = issueType switch
            {
                IssueType.Issue => string.Empty,
                IssueType.PullRequest => @"files(first: " + MaxFileChangesPerPR + @") {
                    totalCount
                    nodes {
                        path
                    }
                    pageInfo {
                        hasNextPage
                        endCursor
                    }
                }",
                _ => throw new ArgumentOutOfRangeException(nameof(issueType)),
            };
            var issueNodeName = issueType switch
            {
                IssueType.Issue => "issues", // Query for issues
                IssueType.PullRequest => "issues:pullRequests", // Query for pull requests, but rename the node to 'issues' to re-use code
                _ => throw new ArgumentOutOfRangeException(nameof(issueType)),
            };

            var issueRequest = new GraphQLRequest(
                query: @"query ($owner: String!, $name: String!, $afterIssue: String) {
  repository(owner: $owner, name: $name) {
    name
    " + issueNodeName + @"(after: $afterIssue, first: 100, orderBy: {field: CREATED_AT, direction: DESC}) {
      nodes {
        number
        author {
          login
        }" + prSpecific + @"
        title
        bodyText
        createdAt
        labels(first: 10) {
          nodes {
            name
          },
          totalCount
        }
      }
      pageInfo {
        hasNextPage
        endCursor
      }
      totalCount
    }
  }
}
",
                variables: new
                {
                    owner = owner,
                    name = repo,
                    afterIssue = afterID,
                });

            var result = await ghGraphQL.SendQueryAsync<Data<T>>(issueRequest);
            if (result.Errors?.Any() ?? false)
            {
                Trace.WriteLine($"GraphQL errors! ({result.Errors.Length})");
                foreach (var error in result.Errors)
                {
                    Trace.WriteLine($"\t{error.Message}");
                }
                return new GitHubListPage<T> { IsError = true, };
            }

            var issueList = new GitHubListPage<T>
            {
                Issues = result.Data,
            };

            return issueList;
        }
    }
}
