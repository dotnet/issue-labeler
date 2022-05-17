using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;

class Program
{
    public class TestOptions
    {
        public IList<string> TestUrls { get; set; } = null!;
    }

    static async Task Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appSettings.json")
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

                logger.LogInformation(4, "Found {NUM_TEST_URLS} test URLs in configuration", options.TestUrls.Count);

                for (int i = 0; i < options.TestUrls.Count; i++)
                {
                    var testUrl = options.TestUrls[i];
                    logger.LogInformation(5, "Testing URL {URL_NUM}/{NUM_TEST_URLS}: {TEST_URL}", (i + 1), options.TestUrls.Count, testUrl);

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

            logger.LogInformation(6, "Done testing all URLs!");
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
            var stringResponse = await result.Content.ReadAsStringAsync();
            return stringResponse;
        }
        return default;
    }

    public static async Task<object?> GetLabelResponse(HttpClient client, Uri u)
    {
        HttpResponseMessage result = await client.GetAsync(u);
        if (result.IsSuccessStatusCode)
        {
            var stringResponse = await result.Content.ReadAsStringAsync();
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
