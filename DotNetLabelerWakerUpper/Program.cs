using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

class Program
{
    public class TestOptions
    {
        public IEnumerable<string> TestUrls { get; set; } = null!;
    }

    static async Task Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                // the github app for dotnet
                { "TestUrls:0", "https://dotnet-runtime-issue-labeler.azurewebsites.net/api/WebhookIssue/dotnet/docker-tools/851" },
                { "TestUrls:1", "https://dotnet-runtime-issue-labeler.azurewebsites.net/api/WebhookIssue/dotnet/docker-tools/853" },
                { "TestUrls:2", "https://dotnet-runtime-issue-labeler.azurewebsites.net/api/WebhookIssue/dotnet/dotnet-api-docs/7080" },
                { "TestUrls:3", "https://dotnet-runtime-issue-labeler.azurewebsites.net/api/WebhookIssue/dotnet/dotnet-api-docs/7083" },
                { "TestUrls:4", "https://dotnet-runtime-issue-labeler.azurewebsites.net/api/WebhookIssue/dotnet/dotnet-buildtools-prereqs-docker/357" },
                { "TestUrls:5", "https://dotnet-runtime-issue-labeler.azurewebsites.net/api/WebhookIssue/dotnet/dotnet-buildtools-prereqs-docker/454" },
                { "TestUrls:6", "https://dotnet-runtime-issue-labeler.azurewebsites.net/api/WebhookIssue/dotnet/dotnet-docker/3076" },
                { "TestUrls:7", "https://dotnet-runtime-issue-labeler.azurewebsites.net/api/WebhookIssue/dotnet/dotnet-docker/3088" },
                // note: ignore any update to archived repos: dotnet/corefx and dotnet/extensions
                { "TestUrls:8", "https://dotnet-runtime-issue-labeler.azurewebsites.net/api/WebhookIssue/dotnet/runtime/58400"},
                { "TestUrls:9", "https://dotnet-runtime-issue-labeler.azurewebsites.net/api/WebhookIssue/dotnet/runtime/58401"},
                { "TestUrls:10", "https://dotnet-runtime-issue-labeler.azurewebsites.net/api/WebhookIssue/dotnet/sdk/20350"},
                { "TestUrls:11", "https://dotnet-runtime-issue-labeler.azurewebsites.net/api/WebhookIssue/dotnet/sdk/20349"},
                { "TestUrls:12", "https://dotnet-roslyn-labeler.azurewebsites.net/api/WebhookIssue/dotnet/roslyn/56019"},
                { "TestUrls:13", "https://dotnet-roslyn-labeler.azurewebsites.net/api/WebhookIssue/dotnet/roslyn/56014"},
                { "TestUrls:14", "https://dotnet-roslyn-labeler.azurewebsites.net/api/WebhookIssue/dotnet/roslyn/2403"},
                { "TestUrls:15", "https://dotnet-roslyn-labeler.azurewebsites.net/api/WebhookIssue/dotnet/roslyn/2340"},
                // hubbup uses these ones
                { "TestUrls:16", "https://dotnet-aspnetcore-labeler.azurewebsites.net/api/WebhookIssue/dotnet/aspnetcore/35962"},
                { "TestUrls:17", "https://dotnet-aspnetcore-labeler.azurewebsites.net/api/WebhookIssue/dotnet/aspnetcore/35941"},
                { "TestUrls:18", "https://dotnet-aspnetcore-labeler.azurewebsites.net/api/WebhookIssue/dotnet/aspnetcore/35886"},
                { "TestUrls:19", "https://dotnet-aspnetcore-labeler.azurewebsites.net/api/WebhookIssue/dotnet/maui/3581"},
                { "TestUrls:20", "https://dotnet-aspnetcore-labeler.azurewebsites.net/api/WebhookIssue/dotnet/maui/3582"},
                { "TestUrls:21", "https://dotnet-aspnetcore-labeler.azurewebsites.net/api/WebhookIssue/dotnet/roslyn/56019"},
                { "TestUrls:22", "https://dotnet-aspnetcore-labeler.azurewebsites.net/api/WebhookIssue/dotnet/roslyn/56014"},
                // the github app for microsoft org
                { "TestUrls:23", "https://microsoft-dotnet-framework-docker.azurewebsites.net/api/webhookissue/microsoft/dotnet-framework-docker/896"},
                { "TestUrls:24", "https://microsoft-dotnet-framework-docker.azurewebsites.net/api/webhookissue/microsoft/dotnet-framework-docker/100"},
            })
            .Build();
        TestOptions options = config.Get<TestOptions>()!;

        using (var textWriterTraceListener = new TextWriterTraceListener(@"trace.log"))
        {
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder
                .AddSimpleConsole()
                .AddTraceSource(new SourceSwitch("TraceLogs") { Level = SourceLevels.All }, textWriterTraceListener)
            );

            ILogger<Program> logger = loggerFactory.CreateLogger<Program>();
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
                {
                    NoCache = true
                };

                // First wait until model load is finished, add delay, test prediction, then go to next
                foreach (var testUrl in options.TestUrls)
                {
                    var loadUrl = ExtractLoadUrl(testUrl);
                    string? response = await GetResponse(client, new Uri(loadUrl));
                    if (response is null || response.Contains("not yet configured") || response.Contains("Only predictions for"))
                    {
                        string reason = response == null ? "app not responding" : response;
                        logger.LogInformation(0, "Skipping - {Reason}", reason);
                        continue;
                    }

                    while (response!.Contains("loading"))
                    {
                        Thread.Sleep(5000);
                        logger.LogInformation(1, "5 second delay - {Reason}", response);
                        response = await GetResponse(client, new Uri(loadUrl));
                    }

                    Debug.Assert(response!.Contains("Loaded"));
                    var labelResponse = await GetLabelResponse(client, new Uri(testUrl));
                    if (labelResponse != null)
                    {
                        var labelSuggestion = labelResponse as LabelSuggestion;
                        if (labelSuggestion != null && labelSuggestion.LabelScores != null)
                        {
                            logger.LogInformation(2, "Top score for `{Item}` is {TopScoreLabelName}",
                                testUrl.AsSpan().Slice(ExtractEndpointIndex(testUrl)).ToString(),
                                labelSuggestion.LabelScores.First().LabelName);
                        }
                        else
                        {
                            logger.LogInformation(3, "message response - {Response}", labelResponse);
                        }
                    }
                }
            }
        }
    }

    private static string ExtractLoadUrl(string testUrl)
    {
        var span = testUrl.AsSpan();
        var endpointIndex = ExtractEndpointIndex(testUrl);
        return span.Slice(0, endpointIndex).ToString() + "load/"
            + span.Slice(endpointIndex, testUrl.LastIndexOf("/") - endpointIndex).ToString();
    }

    private static int ExtractEndpointIndex(string testUrl) => testUrl.ToLowerInvariant().IndexOf("webhookissue/", StringComparison.Ordinal) + 13;

    public static async Task<string?> GetResponse(HttpClient client, Uri u)
    {
        HttpResponseMessage result = await client.GetAsync(u);
        if (result.IsSuccessStatusCode)
        {
            var stringResponse = result.Content.ReadAsStringAsync().Result;
            return stringResponse;
        }
        return default;
    }

    public static async Task<object?> GetLabelResponse(HttpClient client, Uri u)
    {
        HttpResponseMessage result = await client.GetAsync(u);
        if (result.IsSuccessStatusCode)
        {
            var stringResponse = result.Content.ReadAsStringAsync().Result;
            if (stringResponse != null && stringResponse.Contains("labelName", StringComparison.OrdinalIgnoreCase))
                return await result.Content.ReadFromJsonAsync<LabelSuggestion>();
            return stringResponse;
        }
        return default;
    }

    public class LabelAreaScore
    {
        public string? LabelName { get; set; }
        public float Score { get; set; }
    }
    public class LabelSuggestion
    {
        public List<LabelAreaScore>? LabelScores { get; set; }
    }
}
