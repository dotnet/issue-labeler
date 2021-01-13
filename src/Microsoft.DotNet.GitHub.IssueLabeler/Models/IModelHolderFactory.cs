using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Microsoft.DotNet.Github.IssueLabeler.Models
{
    // TODO: use this in the future to allow loading engines for more than one repo in a single app
    public interface IModelHolderFactory
    {
        //IModelHolder CreateModelHolder(string owner, string repo);
    }

    public class ModelHolderFactory : IModelHolderFactory
    {
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;
        private readonly ConcurrentDictionary<(string, string), IModelHolder> _models = new ConcurrentDictionary<(string, string), IModelHolder>();

        public ModelHolderFactory(ILogger logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        /// <inheritdoc />
        //public IModelHolder CreateModelHolder(string owner, string repo)
        //{
        //    return _models.TryGetValue((owner, repo), out IModelHolder modelHolder) ?
        //        modelHolder :
        //        _models.GetOrAdd((owner, repo), new ModelHolder(_logger, _configuration));
        //}
    }
}