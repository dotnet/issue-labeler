﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using PredictionEngine;
using System.Collections.Concurrent;

namespace PredictionService;

public class AzureBlobModelHolderFactory : IModelHolderFactory
{
    private readonly ConcurrentDictionary<(string, string), IModelHolder> _models = new ConcurrentDictionary<(string, string), IModelHolder>();
    private readonly ILogger<AzureBlobModelHolderFactory> _logger;
    private readonly IConfiguration _configuration;
    private readonly BackgroundTaskQueue _backgroundTaskQueue;

    public AzureBlobModelHolderFactory(
        ILogger<AzureBlobModelHolderFactory> logger,
        IConfiguration configuration,
        BackgroundTaskQueue backgroundTaskQueue)
    {
        _backgroundTaskQueue = backgroundTaskQueue;
        _configuration = configuration;
        _logger = logger;
    }

    public IModelHolder CreateModelHolder(string owner, string repo)
    {
        if (!IsConfigured(repo))
            return null;
        return _models.TryGetValue((owner, repo), out IModelHolder modelHolder) ?
            modelHolder :
           _models.GetOrAdd((owner, repo), InitFor(repo));
    }

    private bool IsConfigured(string repo)
    {
        // the following four configuration values are per repo values.
        string configSection = $"IssueModel:{repo}:PathPrefix";
        if (!string.IsNullOrEmpty(_configuration[configSection]))
        {
            configSection = $"IssueModel:{repo}:BlobName";
            if (!string.IsNullOrEmpty(_configuration[configSection]))
            {
                configSection = $"PrModel:{repo}:PathPrefix";
                if (!string.IsNullOrEmpty(_configuration[configSection]))
                {
                    // has both pr and issue config - allowed
                    configSection = $"PrModel:{repo}:BlobName";
                    return !string.IsNullOrEmpty(_configuration[configSection]);
                }
                else
                {
                    // has issue config only - allowed
                    configSection = $"PrModel:{repo}:BlobName";
                    return string.IsNullOrEmpty(_configuration[configSection]);
                }
            }
        }
        return false;
    }

    private IModelHolder InitFor(string repo)
    {
        var mh = new AzureBlobModelHolder(_logger, _configuration, repo);
        if (!mh.LoadRequested)
        {
            _backgroundTaskQueue.QueueBackgroundWorkItem((ct) => mh.LoadEnginesAsync());
        }
        return mh;
    }
}
