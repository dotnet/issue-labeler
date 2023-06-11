using GitHubHelpers;
using Microsoft.Extensions.Configuration;
using ModelTester;
using PredictionEngine;

if (args.Length != 3)
{
    PrintHelp(errorMessages: $@"ERROR: Required parameters missing: dotnet run -- C:\PATH\TO\ZIPS OWNER/REPO ISSUE_OR_PR_NUMBER");
    return 1;
}

var pathToZips = args[0];
var repoAndOwner = args[1];

var repoAndOwnerParts = repoAndOwner.Split('/');
if (repoAndOwnerParts.Length != 2)
{
    PrintHelp(errorMessages: $@"ERROR: Repo and owner format is incorrect. It must be in the form: OWNER/REPO");
    return 1;
}
(var owner, var repo) = (repoAndOwnerParts[0], repoAndOwnerParts[1]);

if (!int.TryParse(args[2], out int number))
{
    PrintHelp($@"ERROR: Issue or PR number format is incorrect. It must be an int.");
    return 1;
}

var factory = new LocalFileModelHolderFactory(pathToZips);
var modelHolder = factory.CreateModelHolder(owner, repo);

var config = new ConfigurationBuilder()
    .AddUserSecrets("dotnet-issue-labeler")
    .Build();

var gitHubClientWrapper = new GitHubClientWrapper(new OAuthGitHubClientFactory(config));
var predictor = new Predictor(gitHubClientWrapper, modelHolder);
var labelSuggestions = await predictor.Predict(owner, repo, number);

PrintLines($"Label predictions for {owner}/{repo}#{number}:");
PrintLines(labelSuggestions.LabelScores.Select(s => $"    {s.LabelName} ({s.Score.ToString("0.######")})").ToArray());

return 0;

static void PrintHelp(params string[]? errorMessages)
{
    Console.WriteLine("DotNet Labeler Updater");
    Console.WriteLine("Tool to upload machine-generated models for issue prediction to Azure storage.");
    if (errorMessages is not null)
    {
        Console.WriteLine();
        PrintLines(errorMessages);
    }
}

static void PrintLines(params string[]? messageLines)
{
    if (messageLines is not null)
    {
        foreach (var errorMessage in messageLines)
        {
            Console.WriteLine(errorMessage);
        }
    }
}
