
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.GitHub.IssueLabeler;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Github.IssueLabeler.Controllers
{
    [Route("api/queue")]
    public class QueueController : Controller
    {
        private ILogger<QueueController> Logger { get; set; }
        private IQueueHelper _queueHelper;
        private readonly bool _inPreview = true;
        private readonly ILabeler _labeler;
        private readonly bool _canCommentOnIssue;
        private readonly bool _canUpdateIssue;
        private readonly IBackgroundTaskQueue _backgroundTaskQueue;

        public QueueController(
            ILabeler labeler,
            IConfiguration configuration,
            IQueueHelper queueHelper,
            ILogger<QueueController> logger,
            IBackgroundTaskQueue backgroundTaskQueue)
        {
            _queueHelper = queueHelper;
            _labeler = labeler;
            _canCommentOnIssue = configuration.GetSection("CanCommentOnIssueFromQ").Get<bool>();
            _canUpdateIssue = configuration.GetSection("CanUpdateIssueFromQ").Get<bool>();
            Logger = logger;
            _backgroundTaskQueue = backgroundTaskQueue;
        }

        [HttpGet("todo/{owner}/{repo}/{id}")]
        public IActionResult QueueTaskForLater(string owner, string repo, int id)
        {
            if (_inPreview)
            {
                return Ok("feature in preview");
            }
            Logger.LogInformation("! Predict later for: {Owner}/{Repo}#{IssueNumber}", owner, repo, id);

            _backgroundTaskQueue.QueueBackgroundWorkItem((ct) => _queueHelper.InsertMessageTask($"Manually added TODO - Dispatch labels for: /{owner}/{repo}#{id}"));
            string msg = $"! Added task for later: {owner}/{repo}#{id}";
            return Ok(msg);
        }

        [HttpGet("message/{msg}")]
        public async Task<IActionResult> AddMessageToQueue(string msg)
        {
            if (_inPreview)
            {
                return Ok("feature in preview");
            }
            await Task.Delay(1);
            await _queueHelper.InsertMessageTask(msg);
            return Ok("! Added message.");
        }

        [HttpGet("backlog/{owner}/{repo}")]
        public async Task<IActionResult> AddTasksFromQueue(string owner, string repo)
        {
            if (_inPreview)
            {
                return Ok("feature in preview");
            }
            await Task.Delay(1);
            var pendingIssues = await _queueHelper.FindPendingIssues(owner, repo);
            foreach ((string owner, string repo, int id) pending in pendingIssues)
            {
                // def ow and rep match owner and repo... cleanup later.
                Logger.LogInformation("! Would have dipatched labels from queue for: {Owner}/{Repo}#{IssueNumber}", pending.owner, pending.repo, pending.id);
                //_backgroundTaskQueue.QueueBackgroundWorkItem((ct) => _labeler.DispatchLabelsAsync(_canCommentOnIssue, _canUpdateIssue, pending.id));
            }
            return Ok("! Added Tasks");
        }

        [HttpGet("cleanup")]
        public async Task<IActionResult> CleanupQueue()
        {
            if (_inPreview)
            {
                return Ok("feature in preview");
            }
            await _queueHelper.CleanupQueue();
            return Ok("cleanup ok");
        }
    }
}
