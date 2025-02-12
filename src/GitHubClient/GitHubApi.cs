// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http.Json;
using System.Text;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;

namespace GitHubClient;

public class GitHubApi
{
    private static GraphQLHttpClient CreateGraphQLClient(string githubToken)
    {
        GraphQLHttpClient client = new GraphQLHttpClient(
            "https://api.github.com/graphql",
            new SystemTextJsonSerializer()
        );

        client.HttpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                scheme: "bearer",
                parameter: githubToken);

        client.HttpClient.Timeout = TimeSpan.FromMinutes(2);

        return client;
    }

    private static HttpClient CreateRestClient(string githubToken)
    {
        HttpClient client = new();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            scheme: "bearer",
            parameter: githubToken);
        client.DefaultRequestHeaders.Accept.Add(new("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.Add("User-Agent", "GitHub-ML-Labeler");

        return client;
    }

    public static async IAsyncEnumerable<(Issue Issue, string Label)> DownloadIssues(string githubToken, string org, string repo, Predicate<string> labelPredicate, int? issueLimit, int pageSize, int pageLimit, int[] retries, bool verbose = false)
    {
        await foreach (var item in DownloadItems<Issue>("issues", githubToken, org, repo, labelPredicate, issueLimit, pageSize, pageLimit, retries, verbose))
        {
            yield return (item.Item, item.Label);
        }
    }

    public static async IAsyncEnumerable<(PullRequest PullRequest, string Label)> DownloadPullRequests(string githubToken, string org, string repo, Predicate<string> labelPredicate, int? pullLimit, int pageSize, int pageLimit, int[] retries, bool verbose = false)
    {
        var items = DownloadItems<PullRequest>("pullRequests", githubToken, org, repo, labelPredicate, pullLimit, pageSize, pageLimit, retries, verbose);

        await foreach (var item in items)
        {
            yield return (item.Item, item.Label);
        }
    }

    private static async IAsyncEnumerable<(T Item, string Label)> DownloadItems<T>(string itemQueryName, string githubToken, string org, string repo, Predicate<string> labelPredicate, int? itemLimit, int pageSize, int pageLimit, int[] retries, bool verbose) where T : Issue
    {
        pageSize = Math.Min(pageSize, 100);

        int pageNumber = 0;
        string? after = null;
        bool hasNextPage = true;
        int loadedCount = 0;
        int includedCount = 0;
        int? totalCount = null;
        byte retry = 0;
        bool finished = false;

        do
        {
            Console.WriteLine($"Downloading {itemQueryName} page {pageNumber + 1} from {org}/{repo}...{(retry > 0 ? $" (retry {retry} of {retries.Length}) " : "")}{(after is not null ? $" (cursor: '{after}')" : "")}");

            Page<T> page;

            try
            {
                page = await GetItemsPage<T>(githubToken, org, repo, pageSize, after, itemQueryName);
            }
            catch (Exception ex) when (
                ex is HttpIOException ||
                ex is HttpRequestException ||
                ex is GraphQLHttpRequestException ||
                ex is TaskCanceledException
            )
            {
                Console.WriteLine($"Exception caught during query.\n  {ex.Message}");

                if (retry >= retries.Length - 1)
                {
                    Console.WriteLine($"Retry limit of {retries.Length} reached. Aborting.");
                    break;
                }
                else
                {
                    Console.WriteLine($"Waiting {retries[retry]} seconds before retry {retry + 1} of {retries.Length}...");
                    await Task.Delay(retries[retry] * 1000);
                    retry++;

                    continue;
                }
            }

            if (after == page.EndCursor)
            {
                Console.WriteLine($"Paging did not progress. Cursor: '{after}'. Aborting.");
                break;
            }

            pageNumber++;
            after = page.EndCursor;
            hasNextPage = page.HasNextPage;
            loadedCount += page.Nodes.Length;
            totalCount ??= page.TotalCount;
            retry = 0;

            foreach (T item in page.Nodes)
            {
                // If there are more labels, there might be other applicable
                // labels that were not loaded and the model is incomplete.
                if (item.Labels.HasNextPage)
                {
                    if (verbose) Console.WriteLine($"{itemQueryName} {org}/{repo}#{item.Number} - Excluded from output. Not all labels were loaded.");
                    continue;
                }

                // Only items with exactly one applicable label are used for the model.
                string[] labels = Array.FindAll(item.LabelNames, labelPredicate);
                if (labels.Length != 1)
                {
                    if (verbose) Console.WriteLine($"{itemQueryName} {org}/{repo}#{item.Number} - Excluded from output. {labels.Length} applicable labels found.");
                    continue;
                }

                // Exactly one applicable label was found on the item. Include it in the model.
                if (verbose) Console.WriteLine($"{itemQueryName} {org}/{repo}#{item.Number} - Included in output. Applicable label: '{labels[0]}'.");

                yield return (item, labels[0]);

                includedCount++;

                if (itemLimit.HasValue && includedCount >= itemLimit)
                {
                    break;
                }
            }

            finished = (!hasNextPage || pageNumber >= pageLimit || (itemLimit.HasValue && includedCount >= itemLimit));

            Console.WriteLine(
                $"Included: {includedCount} (limit: {(itemLimit.HasValue ? itemLimit : "none")}) | " +
                $"Downloaded: {loadedCount} (total: {totalCount}) | " +
                $"Pages: {pageNumber} (limit: {pageLimit})");
        }
        while (!finished);
    }

    private static async Task<Page<T>> GetItemsPage<T>(string githubToken, string org, string repo, int pageSize, string? after, string itemQueryName) where T : Issue
    {
        using GraphQLHttpClient client = CreateGraphQLClient(githubToken);

        string files = typeof(T) == typeof(PullRequest) ? "files (first: 100) { nodes { path } }" : "";

        GraphQLRequest query = new GraphQLRequest
        {
            Query = $$"""
                query ($owner: String!, $repo: String!, $after: String) {
                    repository (owner: $owner, name: $repo) {
                        result:{{itemQueryName}} (after: $after, first: {{pageSize}}, orderBy: {field: CREATED_AT, direction: DESC}) {
                            nodes {
                                number
                                title
                                body: bodyText
                                labels (first: 25) {
                                    nodes { name },
                                    pageInfo { hasNextPage }
                                }
                                {{files}}
                            }
                            pageInfo {
                                hasNextPage
                                endCursor
                            }
                            totalCount
                        }
                    }
                }
                """,
            Variables = new
            {
                Owner = org,
                Repo = repo,
                After = after
            }
        };

        var response = await client.SendQueryAsync<RepositoryQuery<Page<T>>>(query);

        if (response.Errors?.Any() ?? false)
        {
            string errors = string.Join("\n\n", response.Errors.Select((e, i) => $"{i + 1}. {e.Message}").ToArray());
            throw new ApplicationException($"GraphQL request returned errors.\n\n{errors}");
        }
        else if (response.Data is null || response.Data.Repository is null || response.Data.Repository.Result is null)
        {
            throw new ApplicationException("GraphQL response did not include the repository result data");
        }

        return response.Data.Repository.Result;
    }

    public static async Task<Issue?> GetIssue(string githubToken, string org, string repo, ulong number) =>
        await GetItem<Issue>(githubToken, org, repo, number, "issue");

    public static async Task<PullRequest?> GetPullRequest(string githubToken, string org, string repo, ulong number) =>
        await GetItem<PullRequest>(githubToken, org, repo, number, "pullRequest");

    private static async Task<T?> GetItem<T>(string githubToken, string org, string repo, ulong number, string itemQueryName) where T : Issue
    {
        using GraphQLHttpClient client = CreateGraphQLClient(githubToken);

        string files = typeof(T) == typeof(PullRequest) ? "files (first: 100) { nodes { path } }" : "";

        GraphQLRequest query = new GraphQLRequest
        {
            Query = $$"""
                query ($owner: String!, $repo: String!, $number: Int!) {
                    repository (owner: $owner, name: $repo) {
                        result:{{itemQueryName}} (number: $number) {
                            number
                            title
                            body: bodyText
                            labels (first: 25) {
                                nodes { name },
                                pageInfo { hasNextPage }
                            }
                            {{files}}
                        }
                    }
                }
                """,
            Variables = new
            {
                Owner = org,
                Repo = repo,
                Number = number
            }
        };

        return (await client.SendQueryAsync<RepositoryQuery<T>>(query)).Data.Repository.Result;
    }

    public static async Task<string?> AddLabel(string githubToken, string org, string repo, string type, ulong number, string label)
    {
        using var client = CreateRestClient(githubToken);
        int[] retries = [5, 10, 30];
        byte retry = 0;

        while (retry < retries.Length)
        {
            var response = await client.PostAsJsonAsync(
                $"https://api.github.com/repos/{org}/{repo}/issues/{number}/labels",
                new string[] { label },
                CancellationToken.None);

            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            Console.WriteLine($"""
                [{type} #{number}] Failed to add label '{label}'. {response.ReasonPhrase} ({response.StatusCode})
                    {(retry < retries.Length ? $"Will proceed with retry {retry + 1} of {retries.Length} after {retries[retry]} seconds..." : $"Retry limit of {retries.Length} reached.")}
                """);

            await Task.Delay(retries[retry++] * 1000);
        }

        return $"Failed to add label '{label}' after {retries.Length} retries.";
    }

    public static async Task<string?> RemoveLabel(string githubToken, string org, string repo, string type, ulong number, string label)
    {
        using var client = CreateRestClient(githubToken);
        int[] retries = [5, 10, 30];
        byte retry = 0;

        while (retry < retries.Length)
        {
            var response = await client.DeleteAsync(
                $"https://api.github.com/repos/{org}/{repo}/issues/{number}/labels/{label}",
                CancellationToken.None);

            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            Console.WriteLine($"""
                [{type} #{number}] Failed to remove label '{label}'. {response.ReasonPhrase} ({response.StatusCode})
                    {(retry < retries.Length ? $"Will proceed with retry {retry + 1} of {retries.Length} after {retries[retry]} seconds..." : $"Retry limit of {retries.Length} reached.")}
                """);

            await Task.Delay(retries[retry++] * 1000);
        }

        return $"Failed to remove label '{label}' after {retries.Length} retries.";
    }
}
