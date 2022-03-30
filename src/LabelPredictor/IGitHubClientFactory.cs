using Octokit;
using System.Threading.Tasks;

namespace Microsoft.DotNet.GitHub.IssueLabeler.Data
{
    public interface IGitHubClientFactory
    {
        Task<GitHubClient> CreateAsync();
    }
}
