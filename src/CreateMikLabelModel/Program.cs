// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CreateMikLabelModel.DL;
using CreateMikLabelModel.ML;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace CreateMikLabelModel
{
    public class Program
    {
        private static string GetPrettyName(string file) => $"\"{Path.GetFileName(file)}\"";

        private static async Task<int> Main(string[] args)
        {
            var startTime = DateTimeOffset.UtcNow;
            var folder = Directory.GetCurrentDirectory();
            using var textWriterTraceListener = new TextWriterTraceListener(Path.Combine(folder, "trace.log"));
            using var consoleTraceListener = new ConsoleTraceListener();
            Trace.Listeners.Add(textWriterTraceListener);
            Trace.Listeners.Add(consoleTraceListener);
            Trace.WriteLine($"Output files will be created in folder: {folder}");

            var repoJsonFileName =
                Path.Combine(
                    Path.GetDirectoryName(typeof(Program).Assembly.Location),
                    "repos.json");

            if (!File.Exists(repoJsonFileName))
            {
                Trace.TraceError($"The repo definition file couldn't be found at {repoJsonFileName}");
                return 1;
            }
            var repoJsonContents = File.ReadAllText(repoJsonFileName);
            Trace.WriteLine($"Loaded repo list from {repoJsonFileName}");

            var repoList = JsonSerializer.Deserialize<IDictionary<string, string[][]>>(repoJsonContents, new JsonSerializerOptions() { ReadCommentHandling = JsonCommentHandling.Skip, });

            var repoArrays = repoList["repos"];

            if (args.Length != 1)
            {
                Trace.TraceError($"A repo must be specified as a command line argument.");
                Trace.WriteLine($"Usage:");
                Trace.WriteLine($"  dotnet run -- [repo]");
                Trace.WriteLine($"");
                Trace.WriteLine($"Arguments:");
                Trace.WriteLine($"  [repo] is one of the following repos:");
                var sortedRepoArrays = repoArrays.OrderBy(rs => rs[0], StringComparer.OrdinalIgnoreCase);
                foreach (var repoSet in sortedRepoArrays)
                {
                    Trace.WriteLine($"    {repoSet[0]}{GetExtraRepoInfo(repoSet)}");
                }
                return 1;
            }

            var repoSetArg = args[0];
            var selectedRepoSet = repoArrays.SingleOrDefault(rs => string.Equals(rs[0], repoSetArg, StringComparison.OrdinalIgnoreCase));
            if (selectedRepoSet == null)
            {
                Trace.TraceError($"The repo '{repoSetArg}' was not found in {repoJsonFileName}");
                return 1;
            }

            Trace.WriteLine($"Selected repo {selectedRepoSet[0]}{GetExtraRepoInfo(selectedRepoSet)}");

            var repoCombo = selectedRepoSet.Select(repo => GetRepoFromString(repo)).ToArray();
            var customFilenamePrefix = $"{repoCombo[0].owner}-{repoCombo[0].repo}-";
            var issueFiles = new DataFilePaths(folder, customFilenamePrefix, forPrs: false, skip: false);
            var prFiles = new DataFilePaths(folder, customFilenamePrefix, forPrs: true, skip: false);

            // 1. Download and save GitHub issues and PRs into a single tab delimited compact tsv file (one record per line)
            if (await DownloadHelper.DownloadItemsAsync(issueFiles.InputPath, repoCombo) == -1)
            {
                return -1;
            }

            // 2. Segment Issues/PRs into Train, Validate, and Test data (80-10-10 percent ratio) and save intermediate files
            var dm = new DatasetModifier(targetRepo: repoCombo[0].repo);
            Trace.WriteLine($"Reading input TSV {GetPrettyName(issueFiles.InputPath)} for {repoCombo[0].owner}/{repoCombo[0].repo}...");

            await DatasetHelper.PrepareAndSaveDatasetsForIssuesAsync(issueFiles, dm);
            Trace.WriteLine($"Saved intermediate train/validate/test files for Issue model training:");
            Trace.WriteLine($"\t{GetPrettyName(issueFiles.TrainPath)}, {GetPrettyName(issueFiles.ValidatePath)}, {GetPrettyName(issueFiles.TestPath)}");

            await DatasetHelper.PrepareAndSaveDatasetsForPrsAsync(prFiles, dm);
            Trace.WriteLine($"Saved intermediate train/validate/test files for PR model training:");
            Trace.WriteLine($"\t{GetPrettyName(prFiles.TrainPath)}, {GetPrettyName(prFiles.ValidatePath)}, {GetPrettyName(prFiles.TestPath)}");

            // 3. Train data and use *-final-model.zip in the deployed app.
            var mlHelper = new MLHelper();

            Trace.WriteLine($"First train issues using train/validate/test files");
            mlHelper.Train(issueFiles, forPrs: false);

            Trace.WriteLine($"Next to train PRs using train/validate/test files");
            mlHelper.Train(prFiles, forPrs: true);

            // 4. Optionally call mlHelper.Test(..) API to see how the intermediate model (*-fitted-model.zip) is behaving on the test data (the last 10% of downloaded issue/PRs).
            Trace.WriteLine($"Testing generated intermediate issue model {GetPrettyName(issueFiles.FittedModelPath)}, against test data: {GetPrettyName(issueFiles.TestPath)}");
            mlHelper.Test(issueFiles, forPrs: false);
            Trace.WriteLine($"Testing generated intermediate PR model {GetPrettyName(prFiles.FittedModelPath)}, against test data: {GetPrettyName(prFiles.TestPath)}");
            mlHelper.Test(prFiles, forPrs: true);

            Trace.WriteLine(new string('-', 80));
            var endTime = DateTimeOffset.UtcNow;
            var deltaTime = endTime - startTime;
            Trace.WriteLine($"Elapsed time: {deltaTime}");

            Trace.WriteLine(new string('-', 80));
            Trace.WriteLine($"Please remember to copy the final ZIP files {GetPrettyName(issueFiles.FinalModelPath)} and {GetPrettyName(prFiles.FinalModelPath)} to Issue Prediction service's blob storage");
            Trace.WriteLine("");

            Trace.WriteLine($"Exiting application.");

            return 0;
        }

        private static (string owner, string repo) GetRepoFromString(string ownerAndRepo)
        {
            var parts = ownerAndRepo.Split('/');
            return (parts[0], parts[1]);
        }

        private static string GetExtraRepoInfo(string[] repoSet)
        {
            if (repoSet.Length == 1)
            {
                return string.Empty;
            }
            return $" (also includes: {string.Join(", ", repoSet.Skip(1))})";
        }
    }
}
