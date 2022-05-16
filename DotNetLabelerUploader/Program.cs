using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text.RegularExpressions;

const string UserSecretKey = "IssueLabelerKey";

if (args.Length != 2)
{
    PrintHelp(errorMessages: $@"ERROR: Required parameters missing: dotnet run -- C:\PATH\TO\ZIPS OWNER/REPO");
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
        $"ERROR: Couldn't find User Secret named '{UserSecretKey}' in configuration. To set or update the key, run 'dotnet user-secrets set {UserSecretKey} AZURE_KEY_HERE' from this project's directory.",
        $"If you don't have a key, go to the Azure Portal, go to the Storage account, select Access keys, and copy the Key value for key1 or key2.");
    return 1;
}
var container = new BlobContainerClient(azureStorageConnectionString, "areamodels");

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
await UploadZipToBlob(sourceZip: zipPaths.Value.pathToIssueModelZip, zipType: "il", owner, repo, candidateIssueBlobZips, container);

if (zipPaths.Value.pathToPRModelZip is not null)
{
    Console.WriteLine("Uploading PRs model...");
    await UploadZipToBlob(sourceZip: zipPaths.Value.pathToPRModelZip, zipType: "pr", owner, repo, candidatePRBlobZips, container);
}

return 0;



static async Task UploadZipToBlob(string sourceZip, string zipType, string owner, string repo, List<string> candidateBlobZips, BlobContainerClient container)
{
    Console.WriteLine($"\tSource file: {sourceZip}");

    var nextNumericSuffix = GetNextNumericSuffix(candidateBlobZips);
    var nextFilename = $"{owner}-{repo}-{zipType}-{nextNumericSuffix}.zip";

    Console.WriteLine($"\tDestination blob: {nextFilename}");

    var fileInfo = new FileInfo(sourceZip);
    Console.WriteLine($"\tUploading blob ({fileInfo.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes)...");
    using var zipStream = File.OpenRead(sourceZip);
    var uploadResponse = await container.UploadBlobAsync(nextFilename, zipStream);
    Console.WriteLine($"\tDone! (HTTP status: {uploadResponse.GetRawResponse().Status})");
}

static string GetNextNumericSuffix(List<string> fileNames)
{
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
        .AddUserSecrets("DotNetLabelerUploader.App")
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
        foreach (var errorMessage in errorMessages)
        {
            Console.WriteLine(errorMessage);
        }
    }
}
