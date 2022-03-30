using Microsoft.Extensions.Configuration;
using Octokit;
using System;
using System.Threading.Tasks;

namespace Microsoft.DotNet.GitHub.IssueLabeler.Data
{
    public sealed class OAuthGitHubClientFactory : IGitHubClientFactory
    {
        private readonly IConfiguration _configuration;

        public OAuthGitHubClientFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task<GitHubClient> CreateAsync()
        {
            const string UserSecretKey = "GitHubAccessToken";

            var gitHubAccessToken = _configuration[UserSecretKey];
            if (string.IsNullOrEmpty(gitHubAccessToken))
            {
                throw new InvalidOperationException($"Couldn't find User Secret named '{UserSecretKey}' in configuration.");
            }
            return Task.FromResult(CreateForToken(gitHubAccessToken));
        }

        private static GitHubClient CreateForToken(string token)
        {
            var productInformation = new ProductHeaderValue("issuelabelertemplate");
            var client = new GitHubClient(productInformation)
            {
                Credentials = new Credentials(token)
            };
            return client;
        }
    }
}
