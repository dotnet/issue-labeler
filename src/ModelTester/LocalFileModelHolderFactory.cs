// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using PredictionEngine;
using System.Collections.Concurrent;

namespace ModelTester;

internal class LocalFileModelHolderFactory : IModelHolderFactory
{
    private readonly ConcurrentDictionary<(string, string), IModelHolder> _models = new ConcurrentDictionary<(string, string), IModelHolder>();
    private readonly string _modelRootFolder;

    public LocalFileModelHolderFactory(string modelRootFolder)
    {
        _modelRootFolder = modelRootFolder;
    }

    public IModelHolder CreateModelHolder(string owner, string repo)
    {
        if (!IsConfigured(owner, repo))
            return null!;

        return _models.TryGetValue((owner, repo), out IModelHolder? modelHolder) ?
            modelHolder :
           _models.GetOrAdd((owner, repo), InitFor(owner, repo));
    }

    private bool IsConfigured(string owner, string repo)
    {
        return File.Exists(GetModelPath(owner, repo, "issues"));
    }

    private IModelHolder InitFor(string owner, string repo)
    {
        var issuePath = GetModelPath(owner, repo, "issues");
        var prPath = GetModelPath(owner, repo, "prs");

        var mh = new LocalFileModelHolder(owner, repo, issuePath, prPath);
        mh.LoadEngines();

        return mh;
    }

    /// <summary>
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="repo"></param>
    /// <param name="modelType">Use 'issues' or 'prs' only.</param>
    /// <returns></returns>
    internal string GetModelPath(string owner, string repo, string modelType)
    {
        return Path.Combine(_modelRootFolder, $"{owner}-{repo}-only-{modelType}-final-model.zip");
    }
}
