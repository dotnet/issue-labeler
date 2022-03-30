// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Github.IssueLabeler.Models;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.GitHub.IssueLabeler
{
    [Route("api/WebhookIssue")]
    public class WebhookIssueController : Controller
    {
        private ILabeler _labeler { get; set; }

        private ILogger<WebhookIssueController> Logger { get; set; }
        private readonly IModelHolderFactory _modelHolderFactory;
        private string _owner;

        public WebhookIssueController(
            ILabeler labeler,
            ILogger<WebhookIssueController> logger,
            IConfiguration configuration,
            IModelHolderFactory modelHolderFactory)
        {
            _modelHolderFactory = modelHolderFactory;
            _labeler = labeler;
            Logger = logger;
            _owner = configuration["RepoOwner"];
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
            // TODO: Add a threshold for how many repo engine loads are allowed
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
                // queued hosted serrvice: task to only download and load models
                Logger.LogInformation("! Checked to see if prediction engines were loaded: {Owner}/{Repo}", _owner, repo);
                return Ok($"Loaded {owner}/{repo}");
            }
            return Ok($"Prediction engines for {owner}/{repo} are still loading.");
        }

        [HttpGet("{owner}/{repo}/{id}")]
        public async Task<IActionResult> GetPrediction(string owner, string repo, int id)
        {
            // TODO: Add a threshold for how many repo engine loads are allowed
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
                // queued hosted serrvice: task to only download and load models
                Logger.LogInformation("! Prediction for: {Owner}/{Repo}#{IssueNumber}", owner, repo, id);
                var labelSuggestion = await _labeler.PredictUsingModelsFromStorageQueue(owner, repo, id);
                return Ok(labelSuggestion);
            }

            // TODO test this
            return BadRequest("Models need to load before requesting for predictions. Wait until the models are loaded");
        }
    }
}
