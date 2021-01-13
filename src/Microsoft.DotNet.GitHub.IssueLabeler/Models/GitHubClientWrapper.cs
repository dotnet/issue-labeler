using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DotNet.GitHub.IssueLabeler.Data;
using Octokit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

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
        private readonly GitHubClientFactory _gitHubClientFactory;
        private readonly bool _skipAzureKeyVault;

        public GitHubClientWrapper(
            ILogger<GitHubClientWrapper> logger,
            IConfiguration configuration,
            GitHubClientFactory gitHubClientFactory)
        {
            _skipAzureKeyVault = configuration.GetSection("SkipAzureKeyVault").Get<bool>(); // TODO locally true
            _gitHubClientFactory = gitHubClientFactory;
            _logger = logger;

        }

        // TODO add lambda to remove repetetive logic in this class
        // -> call and pass a lambda calls create, and if fails remake and call it again.

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
    }
}