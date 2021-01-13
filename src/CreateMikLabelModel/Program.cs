// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CreateMikLabelModel.DL;
using CreateMikLabelModel.ML;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace CreateMikLabelModel
{
    public class Program
    {
        private static readonly (string owner, string repo)[][] Repos = new[]
        {
            new[] {
              ("dotnet", "aspnetcore"),   // first item is the target Repo
            },
            new[] {
             ("dotnet", "dotnet-docker"),   // first item is the target Repo
             ("microsoft", "dotnet-framework-docker"),
             ("dotnet", "dotnet-buildtools-prereqs-docker"),
            },
            new[] {
              ("dotnet", "docker-tools"),
            },
            new[] {
              ("dotnet", "extensions"),
            },
            new[] {
               ("dotnet", "runtime"),      // first item is the target Repo
                ("dotnet", "corefx"),   // the rest are archived repositories
            },
            new[] {
             ("dotnet", "iot")
            },
            new[] {
             ("dotnet", "roslyn")
            },
            new[] {
             ("dotnet", "dotnet-api-docs"),
            }
        };

        private static string GetPrettyName(string file) => $"\"{Path.GetFileName(file)}\"";

        private static async Task<int> Main()
        {
            string folder = Directory.GetCurrentDirectory();
            using (var textWriterTraceListener = new TextWriterTraceListener(Path.Combine(folder, "trace.log")))
            using (var consoleTraceListener = new ConsoleTraceListener())
            {
                Trace.Listeners.Add(textWriterTraceListener);
                Trace.Listeners.Add(consoleTraceListener);
                Trace.WriteLine($"About to store output files into folder: {folder}.");
                foreach (var repoCombo in Repos)
                {
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
                    Trace.WriteLine($"Please remember to copy the final ZIP files {GetPrettyName(prFiles.FinalModelPath)} and {GetPrettyName(prFiles.FinalModelPath)} to the web site's ML folder");
                    Trace.WriteLine("");
                }

                Trace.WriteLine($"Exiting application.");

                return 0;
            }
        }
    }
}
