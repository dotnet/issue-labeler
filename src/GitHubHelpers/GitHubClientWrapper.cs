// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using Octokit;

namespace GitHubHelpers;

public interface IGitHubClientWrapper
{
    Task<Octokit.Issue> GetIssue(string owner, string repo, int number);
    Task<Octokit.PullRequest> GetPullRequest(string owner, string repo, int number);
    Task<IReadOnlyList<PullRequestFile>> GetPullRequestFiles(string owner, string repo, int number);
}

public class GitHubClientWrapper : IGitHubClientWrapper
{
    private readonly ILogger<GitHubClientWrapper> _logger;
    private readonly IGitHubClientFactory _gitHubClientFactory;
    private GitHubClient? _client;

    public GitHubClientWrapper(
        ILogger<GitHubClientWrapper> logger,
        IGitHubClientFactory gitHubClientFactory)
    {
        _gitHubClientFactory = gitHubClientFactory;
        _logger = logger;
    }

    private async Task<IGitHubClient> PrepareClient(bool createNewClient = false)
    {
        if (createNewClient)
        {
            _client = null;
        }

        _client ??= await _gitHubClientFactory.CreateAsync();

        return _client;
    }

    private async Task<T> MakeRequestWithRetry<T>(string requestName, Func<IGitHubClient, Task<T>> makeRequest)
    {
        IGitHubClient client = await PrepareClient();

        // If an error occurs, get a new client and retry
        try
        {
            return await makeRequest(client);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error occurred during '{requestName}'. Exception Type: {ex.GetType()}. Message: {ex.Message}.");

            client = await PrepareClient(true);
            return await makeRequest(client);
        }
    }

    public async Task<Octokit.Issue> GetIssue(string owner, string repo, int number)
    {
        return await MakeRequestWithRetry("GetIssue", client => client.Issue.Get(owner, repo, number));
    }

    public async Task<Octokit.PullRequest> GetPullRequest(string owner, string repo, int number)
    {
        return await MakeRequestWithRetry("GetPullRequest", client => client.PullRequest.Get(owner, repo, number));
    }

    public async Task<IReadOnlyList<PullRequestFile>> GetPullRequestFiles(string owner, string repo, int number)
    {
        return await MakeRequestWithRetry("GetPullRequestFiles", client => client.PullRequest.Files(owner, repo, number));
    }
}