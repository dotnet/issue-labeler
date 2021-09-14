// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CreateMikLabelModel.DL.Common;
using Octokit.GraphQL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static Octokit.GraphQL.Variable;
using GraphQLConnection = Octokit.GraphQL.Connection;
using GraphQLProductHeaderValue = Octokit.GraphQL.ProductHeaderValue;

namespace CreateMikLabelModel.DL.GraphQL
{

    public static class OctokitGraphQLDownloadHelper
    {
        private static GraphQLConnection _connection;
        static OctokitGraphQLDownloadHelper()
        {
            const string ProductID = "GitHub.Issue.Labeler";
            const string ProductVersion = "1.0";

            var token = CommonHelper.GetGitHubAuthToken();
            _connection = new GraphQLConnection(new GraphQLProductHeaderValue(ProductID, ProductVersion), GraphQLConnection.GithubApiUri, token);
        }

        public static async Task<bool> DownloadAllIssueAndPrsPerAreaAsync(
            (string owner, string repo)[] repoCombo,
            Dictionary<(DateTimeOffset, long, string), string> outputLines,
            StreamWriter outputWriter)
        {
            (int issuesTotal, int prsTotal) missingCount = (0, 0);
            try
            {
                foreach ((string owner, string repo) repo in repoCombo)
                {
                    Dictionary<string, (int totalIssuesCount, int totalPrsCount)> areasWithCounts = await GetAreaLabels(repo.owner, repo.repo);
                    var areas = areasWithCounts.Keys.ToList();
                    var countPerPrs = areasWithCounts.ToDictionary(x => x.Key, x => x.Value.Item2);
                    var countPerIssues = areasWithCounts.ToDictionary(x => x.Key, x => x.Value.Item1);

                    foreach (var isPr in new bool[] { false, true })
                        foreach (var areaLabel in areas)
                        {
                            string items = isPr ? "PRs" : "Issues";
                            if (isPr)
                            {
                                Trace.WriteLine($"Downloading for area {areaLabel} {items}.");
                                var prsToWrite = await GetPullRequestRecords(areaLabel, repo.owner, repo.repo, countPerPrs);
                                Trace.WriteLine($"Downloaded {prsToWrite.Count()} PRs with {areaLabel} label.");
                                int missing = countPerPrs[areaLabel] - prsToWrite.Count();
                                if (missing > 0)
                                {
                                    Trace.WriteLine($"Possibly missing {missing} items to download later.");
                                    missingCount.prsTotal += missing;
                                }
                                outputLines.AddRange(prsToWrite);
                            }
                            else
                            {
                                Trace.WriteLine($"Downloading for area {areaLabel} {items}.");
                                var issuesToWrite = await GetIssueRecords(areaLabel, repo.owner, repo.repo, countPerIssues);
                                Trace.WriteLine($"Downloaded {issuesToWrite.Count()} Issues with {areaLabel} label.");
                                int missing = countPerIssues[areaLabel] - issuesToWrite.Count();
                                if (missing > 0)
                                {
                                    Trace.WriteLine($"Possibly missing {missing} items to download later.");
                                    missingCount.issuesTotal += missing;
                                }
                                outputLines.AddRange(issuesToWrite);
                            }
                        }
                }
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"{nameof(DownloadAllIssueAndPrsPerAreaAsync)}: {ex.Message}");
                return false;
            }
            finally
            {
                foreach (var rc in repoCombo)
                {
                    outputLines = await OctokitDownloadHelper.DownloadMissingIssueAndPrsAsync(outputLines, rc, missingCount);
                }
                CommonHelper.saveAction(outputLines, outputWriter);
            }
        }

        private static async Task<Dictionary<(DateTimeOffset, long, string), string>> GetPullRequestRecords(
            string areaLabel,
            string owner,
            string repo,
            Dictionary<string, int> countPerArea)
        {
            var query = new Query()
                .Repository(owner: Var("owner"), name: Var("name"))
                .PullRequests(first: 100, after: Var("after"), null, null, null, null, new[] { areaLabel })
                .Select(connection => new
                {
                    connection.PageInfo.EndCursor,
                    connection.PageInfo.HasNextPage,
                    connection.TotalCount,
                    Items = connection.Nodes.Select(issue => new
                    {
                        Number = issue.Number,
                        Files = issue.Files(null, null, null, null).AllPages().Select(x => x.Path).ToList(),
                        AuthorLogin = issue.Author.Login,
                        Body = issue.Body,
                        Title = issue.Title,
                        CreatedAt = issue.CreatedAt
                    }).ToList(),
                }).Compile();

            try
            {
                // For the first page, set `after` to null.
                var vars = new Dictionary<string, object>
                    {
                        { "owner", owner },
                        { "name", repo },
                        { "areaLabel", areaLabel },
                        { "after", null },
                    };

                // Read the first page.
                var result = await _connection.Run(query, vars);
                int timesRetried = 0;
                while (result == null && timesRetried < 5)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    result = await _connection.Run(query, vars);
                    timesRetried++;
                }
                if (result == null)
                {
                    Trace.WriteLine($"skipping PRs entirely for {owner}/{repo} those labeled {areaLabel} and moving on.");
                    Trace.WriteLine($"there were supposed to be {countPerArea[areaLabel]} PRs with this label and downloaded none.");
                    return new Dictionary<(DateTimeOffset, long, string), string>();
                }

                // If there are more pages, set `after` to the end cursor.
                vars["after"] = result.HasNextPage ? result.EndCursor : null;

                try
                {
                    while (vars["after"] != null)
                    {
                        // Read the next page.
                        var page = await _connection.Run(query, vars);

                        timesRetried = 0;
                        while (page == null && timesRetried < 5)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5));
                            page = await _connection.Run(query, vars);
                            timesRetried++;
                        }
                        if (page != null)
                        {
                            // Add the results from the page to the result.
                            result.Items.AddRange(page.Items);

                            // If there are more pages, set `after` to the end cursor.
                            vars["after"] = page.HasNextPage ? page.EndCursor : null;
                        }
                        else
                        {
                            Trace.WriteLine($"failed to get some pages for {owner}/{repo} those labeled {areaLabel} and moving on.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex.Message);
                    Trace.WriteLine($"failed to get all page items for {owner}/{repo} labeled {areaLabel}.");
                    Trace.WriteLine($"taking {result.Items.Count} from {result.TotalCount} and moving on.");
                }
                return result.Items.ToDictionary(x => (x.CreatedAt, (long)x.Number, repo), x => CommonHelper.GetCompressedLine(
                    x.Files,
                    areaLabel,
                    x.AuthorLogin,
                    x.Body,
                    x.Title,
                    x.CreatedAt,
                    x.Number,
                    repo,
                    isPr: true));

            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
                Trace.WriteLine($"failed to get any PR pages for {owner}/{repo} those labeled {areaLabel} and moving on.");
                return new Dictionary<(DateTimeOffset, long, string), string>();
            }
            finally
            {
                Trace.WriteLine($"Note: area {areaLabel} supposed to contain {countPerArea[areaLabel]} PRs in it.");
            }
        }

        private static async Task<Dictionary<(DateTimeOffset, long, string), string>> GetIssueRecords(
            string areaLabel,
            string owner,
            string repo,
            Dictionary<string, int> countPerArea)
        {
            var query = new Query()
                .Repository(owner: Var("owner"), name: Var("name"))
                .Issues(first: Var("first"), after: Var("after"), null, null, null, new[] { areaLabel }, null, null)
                .Select(connection => new
                {
                    connection.PageInfo.EndCursor,
                    connection.PageInfo.HasNextPage,
                    connection.TotalCount,
                    Items = connection.Nodes.Select(issue => new
                    {
                        Number = issue.Number,
                        AuthorLogin = issue.Author.Login,
                        Body = issue.Body,
                        Title = issue.Title,
                        CreatedAt = issue.CreatedAt
                    }).ToList(),
                }).Compile();

            // For the first page, set `after` to null.
            var vars = new Dictionary<string, object>
                    {
                        { "owner", owner },
                        { "name", repo },
                        { "areaLabel", areaLabel },
                        { "after", null },
                        { "first", 100 },
                    };

            try
            {
                // Read the first page.
                var result = await _connection.Run(query, vars);

                int timesRetried = 0;
                while (result == null && timesRetried < 5)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    result = await _connection.Run(query, vars);
                    timesRetried++;
                }
                if (result == null)
                {
                    Trace.WriteLine($"skipping issues entirely for {owner}/{repo} those labeled {areaLabel} and moving on.");
                    Trace.WriteLine($"there were supposed to be {countPerArea[areaLabel]} issues with this label and downloaded none.");
                    return new Dictionary<(DateTimeOffset, long, string), string>();
                }

                // If there are more pages, set `after` to the end cursor.
                vars["after"] = result.HasNextPage ? result.EndCursor : null;

                try
                {
                    while (vars["after"] != null)
                    {
                        // Read the next page.
                        var page = await _connection.Run(query, vars);

                        timesRetried = 0;
                        while (page == null && timesRetried < 5)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5));
                            page = await _connection.Run(query, vars);
                            timesRetried++;
                        }
                        if (page != null)
                        {
                            // Add the results from the page to the result.
                            result.Items.AddRange(page.Items);

                            // If there are more pages, set `after` to the end cursor.
                            vars["after"] = page.HasNextPage ? page.EndCursor : null;
                        }
                        else
                        {
                            Trace.WriteLine($"failed to get some pages for {owner}/{repo} those labeled {areaLabel} and moving on.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex.Message);
                    Trace.WriteLine($"failed to get all page items for {owner}/{repo} labeled {areaLabel}.");
                    Trace.WriteLine($"taking {result.Items.Count} from {result.TotalCount} and moving on.");
                }
                return result.Items.ToDictionary(x => (x.CreatedAt, (long)x.Number, repo), x => CommonHelper.GetCompressedLine(
                    null,
                    areaLabel,
                    x.AuthorLogin,
                    x.Body,
                    x.Title,
                    x.CreatedAt,
                    x.Number,
                    repo,
                    isPr: false));

            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
                Trace.WriteLine($"failed to get any issue pages for {owner}/{repo} those labeled {areaLabel} and moving on.");
                return new Dictionary<(DateTimeOffset, long, string), string>();
            }
            finally
            {
                Trace.WriteLine($"Note: area {areaLabel} supposed to contain {countPerArea[areaLabel]} PRs in it.");
            }
        }

        private static async Task<Dictionary<string, (int totalIssuesCount, int totalPrsCount)>> GetAreaLabels(string owner, string repo)
        {
            var query = new Query()
                   .Repository(repo, owner)
                   .Select(repository => new
                   {
                       Name = repository.Name,
                       Labels = repository.Labels(null, null, null, null, null, null).AllPages().Select(label => new
                       {
                           Name = label.Name,
                           TotalPrCount = label.PullRequests(null, null, null, null, null, null, null, null, null).TotalCount,
                           TotalIssueCount = label.Issues(null, null, null, null, null, null, null, null).TotalCount,
                       }).ToDictionary(x => x.Name, x => x)
                   }).Compile();

            var result = await _connection.Run(query);
            var areaLabels = result.Labels.Where(x => LabelHelper.IsAreaLabel(x.Key));
            return areaLabels.ToDictionary(
                x => x.Key,
                x => ((int)x.Value.TotalIssueCount, (int)x.Value.TotalPrCount));
        }

        private static async Task<(Dictionary<(DateTimeOffset, long, string), string>, int)> GetPullRequestRecordsInOneGoAsync(
            string areaLabel,
            string owner,
            string repo)
        {
            var query = new Query()
                   .Repository(repo, owner)
                   .Select(repository => new
                   {
                       Name = repository.Name,
                       Labels = repository.Labels(null, null, null, null, null, null).AllPages().Select(label => new
                       {
                           Name = label.Name,
                       }).ToDictionary(x => x.Name, x => x),
                       CountPrsWithLabel = repository.PullRequests(null, null, null, null, null, null, new[] { areaLabel }, null, null).TotalCount,
                       PullRequests = repository.PullRequests(null, null, null, null, null, null/*new[] { areaLabel }*/, null, null, null).AllPages().Select(pr => new
                       {
                           Number = pr.Number,
                           Labels = pr.Labels(null, null, null, null, null).AllPages().Select(x => x.Name).ToList(),
                           Files = pr.Files(null, null, null, null).AllPages().Select(x => x.Path).ToList(),
                           AuthorLogin = pr.Author.Login,
                           Body = pr.Body,
                           Title = pr.Title,
                           CreatedAt = pr.CreatedAt
                       }).ToDictionary(x => x.Number, x => x)
                   }).Compile();

            var result = await _connection.Run(query);

            return (result.PullRequests.ToDictionary(x => (x.Value.CreatedAt, (long)x.Value.Number, repo), x => CommonHelper.GetCompressedLine(
                x.Value.Files,
                areaLabel,
                x.Value.AuthorLogin,
                x.Value.Body,
                x.Value.Title,
                x.Value.CreatedAt,
                x.Value.Number,
                repo,
                isPr: true)),
            result.CountPrsWithLabel);
        }

        private static async Task<(Dictionary<(DateTimeOffset, long, string), string>, int)> GetIssueRecordsInOneGoAsync(
            string areaLabel,
            string owner,
            string repo)
        {
            var query = new Query()
                    .Repository(repo, owner)
                    .Select(repository => new
                    {
                        Name = repository.Name,
                        Labels = repository.Labels(null, null, null, null, null, null).AllPages().Select(label => new
                        {
                            Name = label.Name,
                        }).ToDictionary(x => x.Name, x => x),
                        CountIssuesWithLabel = repository.Issues(null, null, null, null, null, new[] { areaLabel }, null, null).TotalCount,
                        Issues = repository.Issues(null, null, null, null, null, new[] { areaLabel }, null, null).AllPages().Select(issue => new
                        {
                            Number = issue.Number,
                            Labels = issue.Labels(null, null, null, null, null).AllPages().Select(x => x.Name).ToList(),
                            AuthorLogin = issue.Author.Login,
                            Body = issue.Body,
                            Title = issue.Title,
                            CreatedAt = issue.CreatedAt
                        }).ToDictionary(x => x.Number, x => x),
                    }).Compile();

            var result = await _connection.Run(query);

            return (result.Issues.ToDictionary(x => (x.Value.CreatedAt, (long)x.Value.Number, repo), x => CommonHelper.GetCompressedLine(
                null,
                areaLabel,
                x.Value.AuthorLogin,
                x.Value.Body,
                x.Value.Title,
                x.Value.CreatedAt,
                x.Value.Number,
                repo,
                isPr: false)),
            result.CountIssuesWithLabel);
        }
    }
}
