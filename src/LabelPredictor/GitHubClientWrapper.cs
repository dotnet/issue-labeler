using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DotNet.GitHub.IssueLabeler.Data;
using Octokit;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Github.IssueLabeler.Models
{
    public interface IGitHubClientWrapper
    {
        Task<Octokit.Issue> GetIssue(string owner, string repo, int number);
        Task<Octokit.PullRequest> GetPullRequest(string owner, string repo, int number);
        Task<IReadOnlyList<PullRequestFile>> GetPullRequestFiles(string owner, string repo, int number);
    }

    public class GitHubClientWrapper : IGitHubClientWrapper
    {
        private readonly ILogger<GitHubClientWrapper> _logger;
        private GitHubClient _client;
        private readonly IGitHubClientFactory _gitHubClientFactory;

        public GitHubClientWrapper(
            ILogger<GitHubClientWrapper> logger,
            IGitHubClientFactory gitHubClientFactory)
        {
            _gitHubClientFactory = gitHubClientFactory;
            _logger = logger;
        }

        // TODO add lambda to remove repetetive logic in this class
        // -> call and pass a lambda calls create, and if fails remake and call it again.

        public async Task<Octokit.Issue> GetIssue(string owner, string repo, int number)
        {
            if (_client == null)
            {
                _client = await _gitHubClientFactory.CreateAsync();
            }
            Octokit.Issue iop = null;
            try
            {
                iop = await _client.Issue.Get(owner, repo, number);
            }
            catch (Exception ex)
            {
                _logger.LogError($"ex was of type {ex.GetType()}, message: {ex.Message}");
                _client = await _gitHubClientFactory.CreateAsync();
                iop = await _client.Issue.Get(owner, repo, number);
            }
            return iop;
        }

        public async Task<Octokit.PullRequest> GetPullRequest(string owner, string repo, int number)
        {
            if (_client == null)
            {
                _client = await _gitHubClientFactory.CreateAsync();
            }
            Octokit.PullRequest iop = null;
            try
            {
                iop = await _client.PullRequest.Get(owner, repo, number);
            }
            catch (Exception ex)
            {
                _logger.LogError($"ex was of type {ex.GetType()}, message: {ex.Message}");
                _client = await _gitHubClientFactory.CreateAsync();
                iop = await _client.PullRequest.Get(owner, repo, number);
            }
            return iop;
        }

        public async Task<IReadOnlyList<PullRequestFile>> GetPullRequestFiles(string owner, string repo, int number)
        {
            if (_client == null)
            {
                _client = await _gitHubClientFactory.CreateAsync();
            }
            IReadOnlyList<PullRequestFile> prFiles = null;
            try
            {
                prFiles = await _client.PullRequest.Files(owner, repo, number);

            }
            catch (Exception ex)
            {
                _logger.LogError($"ex was of type {ex.GetType()}, message: {ex.Message}");
                _client = await _gitHubClientFactory.CreateAsync();
                prFiles = await _client.PullRequest.Files(owner, repo, number);
            }
            return prFiles;
        }
    }
}