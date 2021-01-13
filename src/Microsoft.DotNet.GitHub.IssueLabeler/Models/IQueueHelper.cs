using System.Threading.Tasks;
using Azure.Storage.Queues;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Azure.Storage.Queues.Models;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.DotNet.GitHub.IssueLabeler;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Github.IssueLabeler
{
    public interface IQueueHelper
    {
        Task CleanupQueue();
        Task InsertMessageTask(string msg);
        Task<HashSet<(string, string, int)>> FindPendingIssues(string owner, string repo);
    }

    public class QueueHelper : IQueueHelper
    {
        private readonly IGitHubClientWrapper _gitHubClientWrapper;

        private readonly ILogger<QueueHelper> _logger;
        private readonly IConfiguration _configuration;
        private QueueClient _queueClient;
        private readonly Regex _regex = new Regex(@"/(?<owner>dotnet|microsoft)/(?<repo>coreclr|corefx|core-setup|runtime)#(?<num>\d+)");

        public QueueHelper(IConfiguration configuration,
            ILogger<QueueHelper> logger,
            IGitHubClientWrapper gitHubClientWrapper)
        {
            _logger = logger;
            _gitHubClientWrapper = gitHubClientWrapper;
            _configuration = configuration;
            _queueName = configuration["QueueName"];
        }
        private bool CreateQueue(string queueName)
        {
            try
            {
                // Get the connection string from app settings
                string connectionString = _configuration["QConnectionString"];

                if (_queueClient == null)
                {
                    // Instantiate a QueueClient which will be used to create and manipulate the queue
                    _queueClient = new QueueClient(connectionString, queueName);

                    // Create the queue if it doesn't already exist
                    _queueClient.CreateIfNotExists();
                }

                if (_queueClient.Exists())
                {
                    Console.WriteLine($"Queue created: '{_queueClient.Name}'");
                    return true;
                }
                else
                {
                    Console.WriteLine($"Make sure the Azurite storage emulator running and try again.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}\n\n");
                Console.WriteLine($"Make sure the Azurite storage emulator running and try again.");
                return false;
            }
        }

        private void InsertMessage(string queueName, string message)
        {
            // Get the connection string from app settings
            string connectionString = _configuration["QConnectionString"];

            if (_queueClient == null)
            {
                // Instantiate a QueueClient which will be used to create and manipulate the queue
                _queueClient = new QueueClient(connectionString, queueName);

                // Create the queue if it doesn't already exist
                _queueClient.CreateIfNotExists();
            }

            if (_queueClient.Exists())
            {
                // Send a message to the queue
                _queueClient.SendMessage(message);
            }

            Console.WriteLine($"Inserted: {message}");
        }

        //-----------------------------------------------------
        // Process and remove multiple messages from the queue
        //-----------------------------------------------------
        private async Task CleanupMessages(string queueName)
        {
            // Get the connection string from app settings
            string connectionString = _configuration["QConnectionString"];

            // Instantiate a QueueClient which will be used to manipulate the queue
            if (_queueClient == null)
            {
                // Instantiate a QueueClient which will be used to create and manipulate the queue
                _queueClient = new QueueClient(connectionString, queueName);
            }
            
            bool shouldDelete = _configuration.GetSection("ShouldDeleteQueue").Get<bool>();
            bool shouldUpdate = _configuration.GetSection("ShouldUpdateQueue").Get<bool>();

            if (_queueClient.Exists())
            {
                // Receive and process 20 messages
                QueueMessage[] receivedMessages = _queueClient.ReceiveMessages(20, TimeSpan.FromMinutes(5));

                foreach (QueueMessage message in receivedMessages)
                {
                    var issueFromMessage = GetIssueFromMessage(message.MessageText);

                    // Process (i.e. print) the messages in less than 5 minutes
                    _logger.LogInformation($"processing message message: '{message.MessageText}'");

                    if (issueFromMessage.success)
                    {
                        var isMissingAreaLabel = await IsMissingAreaLabel(issueFromMessage.owner, issueFromMessage.repo, issueFromMessage.num);
                        //if (isMissingAreaLabel)
                        //{
                        //    GetPredictionTest(issueFromMessage.owner, issueFromMessage.repo, issueFromMessage.num);
                        //}
                        // Delete the message
                        if (shouldDelete && !isMissingAreaLabel)
                            await _queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt);
                    }
                    else
                    {
                        // Update the message contents - was missing repo info
                        if (shouldUpdate)
                            await _queueClient.UpdateMessageAsync(message.MessageId,
                                    message.PopReceipt,
                                    "Updated contents",
                                    TimeSpan.FromSeconds(60.0)  // Make it invisible for another 60 seconds
                                );
                    }
                }
            }
        }

        private async Task<bool> IsMissingAreaLabel(string owner, string repo, int number)
        {
            var iop = await _gitHubClientWrapper.GetIssue(owner, repo, number);
            var existingLabelList = iop?.Labels?.Where(x => !string.IsNullOrEmpty(x.Name)).Select(x => x.Name).ToList();
            return !existingLabelList.Where(x => x.StartsWith("area-", StringComparison.OrdinalIgnoreCase)).Any();
        }

        // test first
        public async Task<HashSet<(string, string, int)>> FindPendingIssues(string owner, string repo)
        {
            await Task.Delay(1);
            var issuesPending = new HashSet<(string, string, int)>();
            // Get the connection string from app settings
            string connectionString = _configuration["QConnectionString"];

            // Instantiate a QueueClient which will be used to manipulate the queue
            if (_queueClient == null)
            {
                // Instantiate a QueueClient which will be used to create and manipulate the queue
                _queueClient = new QueueClient(connectionString, _queueName);

                // Create the queue if it doesn't already exist
                _queueClient.CreateIfNotExists();
            }

            if (_queueClient.Exists())
            {
                // Receive and process 20 messages
                QueueMessage[] receivedMessages = _queueClient.ReceiveMessages(20, TimeSpan.FromMinutes(5));

                foreach (QueueMessage message in receivedMessages)
                {
                    var issueFromMessage = GetIssueFromMessage(message.MessageText);

                    // Process (i.e. print) the messages in less than 5 minutes
                    _logger.LogInformation($"processing message message: '{message.MessageText}'");

                    if (issueFromMessage.success)
                    {
                        var isMissingAreaLabel = await IsMissingAreaLabel(owner, repo, issueFromMessage.num);
                        if (isMissingAreaLabel &&
                            owner.Equals(issueFromMessage.owner, StringComparison.OrdinalIgnoreCase) &&
                            repo.Equals(issueFromMessage.repo, StringComparison.OrdinalIgnoreCase))
                        {
                            // TODO test this
                            // what if same issue has multiple messages... I dont want to comment plenty of times
                            if (!issuesPending.Contains((issueFromMessage.owner, issueFromMessage.repo, issueFromMessage.num)))
                            {
                                issuesPending.Add((issueFromMessage.owner, issueFromMessage.repo, issueFromMessage.num));
                            }
                        }
                    }
                }
            }
            return issuesPending;
        }

        public async Task CleanupQueue()
        {
            await Task.Delay(1);
            CreateQueue(_queueName);
            Interlocked.Increment(ref count);
            await CleanupMessages(_queueName);
        }

        private (string owner, string repo, int num, bool success) GetIssueFromMessage(string message)
        {
            var matches = _regex.Matches(message);
            if (matches.Count == 1)
            {
                var number = matches[0].Groups["num"].Value;
                if (int.TryParse(number, out int num))
                {
                    var repo = matches[0].Groups["repo"].Value;
                    var owner = matches[0].Groups["owner"].Value;
                    return (owner, repo, num, true);
                }
            }
            return (default, default, default, false);
        }
        private int count = 0;
        private readonly string _queueName;
        public Task InsertMessageTask(string msg)
        {
            var tasks = new List<Task>();
            tasks.Add(InsertMessageAsync(msg));
            return tasks.First();
        }

        private async Task InsertMessageAsync(string msg)
        {
            await Task.Delay(1);
            CreateQueue(_queueName);
            Interlocked.Increment(ref count);
            InsertMessage(_queueName, $"{count} - {DateTimeOffset.Now} - {msg}");
        }
    }
}