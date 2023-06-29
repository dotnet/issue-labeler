// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitHubHelpers;
using Microsoft.AspNetCore.Mvc;
using PredictionEngine;

namespace PredictionService;

[Route("api/WebhookIssue")]
[Route("api/PredictionEngine")]
public class PredictionEngineController : Controller
{
    private ILogger<PredictionEngineController> _logger { get; set; }
    private string _owner;
    private readonly GitHubClientWrapper _gitHubClientWrapper;
    private readonly IModelHolderFactory _modelHolderFactory;

    public PredictionEngineController(
        ILogger<PredictionEngineController> logger,
        IConfiguration configuration,
        GitHubClientWrapper gitHubClientWrapper,
        IModelHolderFactory modelHolderFactory)
    {
        _logger = logger;
        _owner = configuration["RepoOwner"];
        _gitHubClientWrapper = gitHubClientWrapper;
        _modelHolderFactory = modelHolderFactory;        
    }

    [HttpGet("")]
    [HttpGet("/")]
    [HttpGet("Index")]
    public IActionResult Index()
    {
        return Content($"Check the logs, or predict labels.");
    }

    [HttpGet("load/{owner}/{repo}")]
    public IActionResult ManuallyRequestEnginesLoaded(string owner, string repo)
    {
        if (!owner.Equals(_owner, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest($"Only predictions for {_owner} are supported");
        }

        var modelHolder = _modelHolderFactory.CreateModelHolder(owner, repo);
        if (modelHolder == null)
        {
            return BadRequest($"Repo {_owner}/{repo} is not yet configured for label prediction.");
        }

        if (modelHolder.IsIssueEngineLoaded && (modelHolder.UseIssuesForPrsToo || modelHolder.IsPrEngineLoaded))
        {
            _logger.LogInformation("! Checked to see if prediction engines were loaded: {Owner}/{Repo}", _owner, repo);
            return Ok($"Loaded {owner}/{repo}");
        }

        return Ok($"Prediction engines for {owner}/{repo} are still loading. Issue Engine Loaded: {modelHolder.IsIssueEngineLoaded}. Use Issues for PRs: {modelHolder.UseIssuesForPrsToo}. PR Engine Loaded: {modelHolder.IsPrEngineLoaded}.");
    }

    [HttpGet("{owner}/{repo}/{id}")]
    public async Task<IActionResult> GetPrediction(string owner, string repo, int id)
    {
        if (!owner.Equals(_owner, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest($"Only predictions for {_owner} are supported");
        }

        var modelHolder = _modelHolderFactory.CreateModelHolder(owner, repo);
        if (modelHolder == null)
        {
            return BadRequest($"Repo {_owner}/{repo} is not yet configured for label prediction.");
        }

        if (modelHolder.IsIssueEngineLoaded && (modelHolder.UseIssuesForPrsToo || modelHolder.IsPrEngineLoaded))
        {
            _logger.LogInformation("! Prediction for: {Owner}/{Repo}#{IssueNumber}", owner, repo, id);
            var predictor = new Predictor(_gitHubClientWrapper, modelHolder, _logger);
            var labelSuggestion = await predictor.Predict(owner, repo, id);

            return Ok(labelSuggestion);
        }

        return BadRequest("Models need to load before requesting for predictions. Wait until the models are loaded");
    }
}
