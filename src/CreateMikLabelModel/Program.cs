// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CreateMikLabelModel.DL;
using CreateMikLabelModel.ML;
using System;
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
                ("dotnet", "extensions"),
            },
            new[] {
                ("dotnet", "runtime"),      // first item is the target Repo
                ("dotnet", "extensions"),   // the rest are archived repositories
                ("dotnet", "corefx"),
                ("dotnet", "coreclr"),
                ("dotnet", "core-setup"),
            }
        };

        private static async Task<int> Main()
        {
            var folder = Directory.GetCurrentDirectory();
            foreach (var repoCombo in Repos)
            {
                var customFilenamePrefix = $"{repoCombo[0].owner}-{repoCombo[0].repo}-";
                var issueFiles = new DataFilePaths(folder, customFilenamePrefix, forPrs: false);
                var prFiles = new DataFilePaths(folder, customFilenamePrefix, forPrs: true);

                // 1. Download GitHub issues and PRs into a single tab delimited compact tsv file (one record per line)
                if (await DownloadHelper.DownloadItemsAsync(issueFiles.InputPath, repoCombo) == -1)
                {
                   return -1;
                }

                // 2. Segment Issues/PRs into Train, Validate, and Test data (80-10-10 percent ratio)
                var dm = new DatasetModifier(targetRepo: repoCombo[0].repo);
                Console.WriteLine($"Reading input TSV {issueFiles.InputPath}...");
                await DatasetHelper.PrepareAndSaveDatasetsForIssuesAsync(issueFiles, dm);
                await DatasetHelper.PrepareAndSaveDatasetsForPrsAsync(prFiles, dm);

                // 3. Train data and use *-final-model.zip in the deployed app.
                var mlHelper = new MLHelper();

                Console.WriteLine($"First train issues");
                mlHelper.Train(issueFiles, forPrs: false);

                Console.WriteLine($"Next to train PRs");
                mlHelper.Train(prFiles, forPrs: true);

                // 4. Optionally call mlHelper.Test(..) API to see how the intermediate model (*-fitted-model.zip) is behaving on the test data (the last 10% of downloaded issue/PRs).
                mlHelper.Test(issueFiles, forPrs: false);
                mlHelper.Test(prFiles, forPrs: true);

                Console.WriteLine(new string('-', 80));
                Console.WriteLine();
            }

            Console.WriteLine($"Please remember to copy the ZIP files to the web site's ML folder");

            return 0;
        }
    }
}
