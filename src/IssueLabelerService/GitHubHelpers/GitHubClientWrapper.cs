// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Octokit;

namespace GitHubHelpers;

public interface IGitHubClientWrapper
{
    Task<Octokit.Issue> GetIssue(string owner, string repo, int number);
    Task<Octokit.PullRequest> GetPullRequest(string owner, string repo, int number);
    Task<IReadOnlyList<PullRequestFile>> GetPullRequestFiles(string owner, string repo, int number);
    Task AddLabels(string owner, string repo, int number, IEnumerable<string> labels);
    Task CommentOn(string owner, string repo, int number, string comment);
}
public class GitHubClientWrapper : IGitHubClientWrapper
{
    private readonly ILogger<GitHubClientWrapper> _logger;
    private GitHubClient _client;
    private readonly GitHubClientFactory _gitHubClientFactory;
    private readonly bool _skipAzureKeyVault;

    public GitHubClientWrapper(
        ILogger<GitHubClientWrapper> logger,
        IConfiguration configuration,
        GitHubClientFactory gitHubClientFactory)
    {
        _logger = logger;
        _skipAzureKeyVault = configuration.GetSection("SkipAzureKeyVault").Get<bool>(); // TODO locally true
        _gitHubClientFactory = gitHubClientFactory;

    }

    public async Task<Octokit.Issue> GetIssue(string owner, string repo, int number)
    {
        if (_client == null)
        {
            _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
        }
        Octokit.Issue iop = null;
        try
        {
            iop = await _client.Issue.Get(owner, repo, number);
        }
        catch (Exception ex)
        {
            _logger.LogError($"ex was of type {ex.GetType()}, message: {ex.Message}");
            _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
            iop = await _client.Issue.Get(owner, repo, number);
        }
        return iop;
    }

    public async Task<Octokit.PullRequest> GetPullRequest(string owner, string repo, int number)
    {
        if (_client == null)
        {
            _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
        }
        Octokit.PullRequest iop = null;
        try
        {
            iop = await _client.PullRequest.Get(owner, repo, number);
        }
        catch (Exception ex)
        {
            _logger.LogError($"ex was of type {ex.GetType()}, message: {ex.Message}");
            _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
            iop = await _client.PullRequest.Get(owner, repo, number);
        }
        return iop;
    }

    public async Task<IReadOnlyList<PullRequestFile>> GetPullRequestFiles(string owner, string repo, int number)
    {
        if (_client == null)
        {
            _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
        }
        IReadOnlyList<PullRequestFile> prFiles = null;
        try
        {
            prFiles = await _client.PullRequest.Files(owner, repo, number);

        }
        catch (Exception ex)
        {
            _logger.LogError($"ex was of type {ex.GetType()}, message: {ex.Message}");
            _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
            prFiles = await _client.PullRequest.Files(owner, repo, number);
        }
        return prFiles;
    }

    public async Task AddLabels(string owner, string repo, int number, IEnumerable<string> labels)
    {
        if (_client == null)
        {
            _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
        }
        try
        {
            await _client.Issue.Labels.AddToIssue(owner, repo, number, labels.ToArray());
        }
        catch (Exception ex)
        {
            // Log the error and retry the operation once
            _logger.LogError($"ex was of type {ex.GetType()}, message: {ex.Message}");
            _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
            await _client.Issue.Labels.AddToIssue(owner, repo, number, labels.ToArray());
        }
    }

    // lambda -> call and pass a lambda calls create, and if fails remake and call it again.

    public async Task CommentOn(string owner, string repo, int number, string comment)
    {
        if (_client == null)
        {
            _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
        }
        try
        {
            await _client.Issue.Comment.Create(owner, repo, number, comment);
        }
        catch (Exception ex)
        {
            _logger.LogError($"ex was of type {ex.GetType()}, message: {ex.Message}");
            _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
            await _client.Issue.Comment.Create(owner, repo, number, comment);
        }
    }
}
