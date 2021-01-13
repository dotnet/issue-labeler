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
        private readonly IModelHolder _modelHolder;
        private readonly IBackgroundTaskQueue _backgroundTaskQueue;
        private string _repo;
        private string _owner;

        public WebhookIssueController(
            ILabeler labeler,
            ILogger<WebhookIssueController> logger,
            IConfiguration configuration,
            IModelHolder modelHolder,
            IBackgroundTaskQueue backgroundTaskQueue)
        {
            _modelHolder = modelHolder;
            _labeler = labeler;
            Logger = logger;
            _backgroundTaskQueue = backgroundTaskQueue;
            _owner = configuration["RepoOwner"];
            _repo = configuration["RepoName"];
        }

        [HttpGet("")]
        [HttpGet("/")]
        [HttpGet("Index")]
        public IActionResult Index()
        {
            return Content($"Check the logs, or predict labels.");
        }

        [HttpGet("load")]
        public IActionResult ManuallyRequestEnginesLoaded()
        {
            if (_modelHolder.IsPrEngineLoaded && _modelHolder.IsIssueEngineLoaded)
            {
                // queued hosted serrvice: task to only download and load models
                Logger.LogInformation("! Checked to see if prediction engines were loaded: {Owner}/{Repo}", _owner, _repo);
                return Ok("Loaded");
            }
            // only do this once for per application lifetime for now
            _backgroundTaskQueue.QueueBackgroundWorkItem((ct) => _modelHolder.LoadEnginesAsync());
            return Ok($"Loading prediction engines.");
        }

        [HttpGet("{owner}/{repo}/{id}")]
        public async Task<IActionResult> GetPrediction(string owner, string repo, int id)
        {
            // TODO support loading multiple prediction engines in one app
            if (!owner.Equals(_owner, StringComparison.OrdinalIgnoreCase) ||
                !repo.Equals(_repo, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest($"Only predictions for {_owner}/{_repo} are supported");
            }

            if (_modelHolder.IsIssueEngineLoaded && _modelHolder.IsPrEngineLoaded)
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
