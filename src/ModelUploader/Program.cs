using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text.RegularExpressions;

const string UserSecretKey = "IssueLabelerKey";

if (args.Length != 2)
{
    PrintHelp(errorMessages: $@"ERROR: Required parameters missing: dotnet run -- PATH_TO_ZIPS OWNER/REPO");
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


var zipPaths = GetModelZipPaths(pathToZips, owner, repo);
if (zipPaths is null)
{
    return 1;
}

Console.WriteLine("Connecting to Azure Storage...");

var azureStorageConnectionString = GetAzureConnectionString();
if (string.IsNullOrEmpty(azureStorageConnectionString))
{
    PrintHelp(
        $"ERROR: Couldn't find User Secret named '{UserSecretKey}' in configuration.",
        $"If you don't have a key configured for the app, follow these steps:",
        $"1. Go to https://portal.azure.com/",
        $"2. Go to this subscription: DDFun IaaS Dev Shared Public",
        $"3. Go to this storage resource: dotnetissuelabelerdata",
        $"4. Select Access keys",
        $"5. Copy the Key value for key1 or key2.",
        $"6. Open a command prompt in this project's directory",
        $"7. Run: dotnet user-secrets set {UserSecretKey} AZURE_KEY_HERE");
    return 1;
}
BlobContainerClient container;
try
{
    container = new BlobContainerClient(azureStorageConnectionString, blobContainerName: "areamodels");
}
catch (Exception ex)
{
    PrintHelp(
        $"ERROR: Couldn't connect to Azure Storage: Exception: {ex.Message}",
        $"Check that the user secret set for this application is correct and still valid.",
        $"If you don't have a key configured for the app, follow these steps:",
        $"1. Go to https://portal.azure.com/",
        $"2. Go to this subscription: DDFun IaaS Dev Shared Public",
        $"3. Go to this storage resource: dotnetissuelabelerdata",
        $"4. Select Access keys",
        $"5. Copy the Key value for key1 or key2.",
        $"6. Open a command prompt in this project's directory",
        $"7. Run: dotnet user-secrets set {UserSecretKey} AZURE_KEY_HERE");
    return 1;
}

var candidateIssueBlobZips = new List<string>();
var candidatePRBlobZips = new List<string>();

Console.WriteLine("Checking existing blobs...");
await foreach (var blob in container.GetBlobsAsync())
{
    if (blob.Name.StartsWith($"{owner}-{repo}-il-", StringComparison.OrdinalIgnoreCase) &&
        blob.Name.EndsWith($".zip", StringComparison.OrdinalIgnoreCase))
    {
        candidateIssueBlobZips.Add(blob.Name);
    }

    if (zipPaths.Value.pathToPRModelZip != null)
    {
        if (blob.Name.StartsWith($"{owner}-{repo}-pr-", StringComparison.OrdinalIgnoreCase) &&
            blob.Name.EndsWith($".zip", StringComparison.OrdinalIgnoreCase))
        {
            candidatePRBlobZips.Add(blob.Name);
        }
    }
}


Console.WriteLine("Uploading issues model...");
string uploadedIssuesModelFilename = await UploadZipToBlob(sourceZip: zipPaths.Value.pathToIssueModelZip, zipType: "il", owner, repo, candidateIssueBlobZips, container);

string? uploadedPrModelFilename = null;

if (zipPaths.Value.pathToPRModelZip is not null)
{
    Console.WriteLine("Uploading PRs model...");
    uploadedPrModelFilename = await UploadZipToBlob(sourceZip: zipPaths.Value.pathToPRModelZip, zipType: "pr", owner, repo, candidatePRBlobZips, container);
}

PrintFollowUpInstructions(owner, repo, uploadedIssuesModelFilename, uploadedPrModelFilename);

return 0;



static async Task<string> UploadZipToBlob(string sourceZip, string zipType, string owner, string repo, List<string> candidateBlobZips, BlobContainerClient container)
{
    Console.WriteLine($"\tSource file: {sourceZip}");

    var nextNumericSuffix = GetNextNumericSuffix(candidateBlobZips);
    var nextFilename = $"{owner}-{repo}-{zipType}-{nextNumericSuffix}.zip";

    Console.WriteLine($"\tDestination blob: {nextFilename}");

    var fileInfo = new FileInfo(sourceZip);
    Console.WriteLine($"\tUploading blob ({fileInfo.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes)...");
    using var zipStream = File.OpenRead(sourceZip);
    var uploadResponse = await container.UploadBlobAsync(blobName: nextFilename, zipStream);
    Console.WriteLine($"\tDone! (HTTP status: {uploadResponse.GetRawResponse().Status})");

    return nextFilename;
}

static string GetNextNumericSuffix(List<string> fileNames)
{
    if (!fileNames.Any())
    {
        // If there are no existing files, use a default suffix
        return "00";
    }
    var numericStringSuffixes = fileNames.Select(f => GetNumericPathPart(f)).ToList();
    var maxSuffixValue = numericStringSuffixes.Max(f => int.Parse(f));

    var longestSuffixLength = numericStringSuffixes.Max(n => n.Length);

    var nextSuffixValue = maxSuffixValue + 1;
    var lengthOfNextSuffix = nextSuffixValue.ToString(CultureInfo.InvariantCulture).Length;

    var suffixFormat = (lengthOfNextSuffix > longestSuffixLength)
        ? "D" // If the next suffix is longer than the old suffix, such as going from 2-digit 99 to 3-digit 100, then just use the new suffix
        : ("D" + longestSuffixLength.ToString(CultureInfo.InvariantCulture)); // Otherwise, the next suffix can be formatted to the same length as the old highest suffix

    var formattedNumericSuffix = nextSuffixValue.ToString(suffixFormat, CultureInfo.InvariantCulture);

    return formattedNumericSuffix;

    static string GetNumericPathPart(string filename)
    {
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
        var numericMatch = Regex.Match(fileNameWithoutExtension, @"^.*\-(\d+)$");
        return numericMatch.Groups[1].Value;
    }
}

static (string pathToIssueModelZip, string? pathToPRModelZip)? GetModelZipPaths(string pathToZips, string owner, string repo)
{
    Console.WriteLine($"Looking for model ZIPs in: {pathToZips}");

    var pathToIssueModelZip = Path.Combine(pathToZips, $"{owner}-{repo}-only-issues-final-model.zip");

    if (!File.Exists(pathToIssueModelZip))
    {
        Console.WriteLine($@"ERROR: Could not find required issue model here: {pathToIssueModelZip}");
        return null;
    }
    Console.WriteLine($@"INFO: Found required issue model here: {pathToIssueModelZip}");

    var pathToPRModelZip = Path.Combine(pathToZips, $"{owner}-{repo}-only-prs-final-model.zip");

    if (!File.Exists(pathToPRModelZip))
    {
        Console.WriteLine($@"INFO: Could not find optional PR model here, so skipping PR upload: {pathToPRModelZip}");
        pathToPRModelZip = null;
    }
    else
    {
        Console.WriteLine($@"INFO: Found optional PR model here: {pathToPRModelZip}");
    }
    return (pathToIssueModelZip, pathToPRModelZip);
}

static string? GetAzureConnectionString()
{
    var config = new ConfigurationBuilder()
        .AddUserSecrets("dotnet-issue-labeler")
        .Build();

    var azureConnectionKey = config[UserSecretKey];
    if (string.IsNullOrEmpty(azureConnectionKey))
    {
        return null;
    }
    return $"DefaultEndpointsProtocol=https;AccountName=dotnetissuelabelerdata;AccountKey={azureConnectionKey};EndpointSuffix=core.windows.net";
}

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

static void PrintFollowUpInstructions(string owner, string repo, string uploadedIssuesModelFilename, string? uploadedPrModelFilename)
{
    // NOTE: The keys in this dictionary must all be lowercase!
    var webAppMap = new Dictionary<(string owner, string repo), string>()
    {
        // App Service Plan: dotnet-extensions-labeler
        {("dotnet", "aspnetcore"), "dotnet-aspnetcore-labeler" },
        {("dotnet", "maui"), "dotnet-aspnetcore-labeler" },
        {("dotnet", "msbuild"), "dotnet-aspnetcore-labeler" },

        {("microsoft", "dotnet-framework-docker"), "microsoft-dotnet-framework-docker" },

        {("nuget", "home"), "nuget-home-labeler" },

        // App Service Plan: MicrosoftDotNetGithubIssueLabeler2018092
        {("dotnet", "roslyn"), "dotnet-roslyn-labeler" },
        {("dotnet", "source-build"), "dotnet-roslyn-labeler" },

        // App Service Plan: dotnet-runtime
        {("dotnet", "docker-tools"), "dotnet-runtime-issue-labeler" },
        {("dotnet", "dotnet-api-docs"), "dotnet-runtime-issue-labeler" },
        {("dotnet", "dotnet-buildtools-prereqs-docker"), "dotnet-runtime-issue-labeler" },
        {("dotnet", "dotnet-docker"), "dotnet-runtime-issue-labeler" },
        {("dotnet", "runtime"), "dotnet-runtime-issue-labeler" },
        {("dotnet", "sdk"), "dotnet-runtime-issue-labeler" },
    };

    if (!webAppMap.TryGetValue((owner.ToLowerInvariant(), repo.ToLowerInvariant()), out var webAppService))
    {
        webAppService = "UNKNOWN";
    }

    var prStepApplicabilityWarning = uploadedPrModelFilename is null
        ? "(NOTE: This model has no PR model, so skip this step) "
        : "";

    PrintLines(
        $"",
        $"!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!",
        $"MANUAL STEPS REQUIRED!",
        $"To use the uploaded models in the label predictor service, the appropriate service must be updated to use the new models:",
        $"1. Go to https://portal.azure.com/",
        $"2. Go to this subscription: DDFun IaaS Dev Shared Public",
        $"3. Go to this App Service resource: {webAppService}",
        $"4. Select Configuration",
        $"5. Set Application Setting IssueModel:{repo}:BlobName to: {uploadedIssuesModelFilename}",
        $"6. Set Application Setting IssueModel:{repo}:PathPrefix to a new value, typically just incremented by one (the exact name doesn't matter)",
        $"7. {prStepApplicabilityWarning}Set Application Setting PrModel:{repo}:BlobName to: {uploadedPrModelFilename}",
        $"8. {prStepApplicabilityWarning}Set Application Setting PrModel:{repo}:PathPrefix to a new value, typically just incremented by one (the exact name doesn't matter; common pattern is to use GHM-[ZIP_FILE_NAME_WITHOUT_EXTENSION], such as GHM-aspnetcore-pr-03)",
        $"9. Click Save and accept the confirmation, which will restart the application and start using the new values",
        $"10. Run the DotNetLabelerWakerUpper tool in this repo to re-load the new models and check that they are all working.",
        $"!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!"
        );
}
