// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CreateMikLabelModel.DL.Common;
using CreateMikLabelModel.DL.GraphQL;
using CreateMikLabelModel.Models;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using Octokit;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CreateMikLabelModel.DL
{
    public static class DownloadHelper
    {
        public static async Task<int> DownloadItemsAsync(string outputPath, (string owner, string repo)[] repoCombo)
        {
            bool fastDownload = false;
            var stopWatch = Stopwatch.StartNew();

            using (var outputWriter = new StreamWriter(outputPath))
            {
                CommonHelper.WriteCsvHeader(outputWriter);
                var outputLinesExcludingHeader = new Dictionary<(DateTimeOffset, long, string), string>();
                bool completed = false;

                if (fastDownload)
                {
                    completed = await GraphQLDownloadHelper.DownloadFastUsingGraphQLAsync(outputLinesExcludingHeader, repoCombo, outputWriter);
                }
                else
                {
                    // slower but more comprehensive, includes close to all, skipping only faulty issue/prs
                    completed = await OctokitGraphQLDownloadHelper.DownloadAllIssueAndPrsPerAreaAsync(repoCombo, outputLinesExcludingHeader, outputWriter);
                }
                if (!completed)
                    return -1;
            }

            stopWatch.Stop();
            Trace.WriteLine($"Done writing TSV in {stopWatch.ElapsedMilliseconds}ms");
            return 1;
        }

        public static async Task FindThoseManuallyAssignedForAreas(string filePath, string outputFile, string owner, string[] areas)
        {
            var lines = await File.ReadAllLinesAsync(filePath);

            var areasOfInterest = new List<string>();
            foreach (var area in areas)
            {
                areasOfInterest.AddRange(lines
                    .Where(x => !string.IsNullOrEmpty(x) && x.Split('\t')[0].Split(',').Length == 3)
                    .Where(x => x.Split("\t")[2].Equals(area, StringComparison.OrdinalIgnoreCase))
                    );
            }

            (string repo, int number, string areaLabel, string createdAt, string itself)[] iops = areasOfInterest
                .Select(aLine => (a: aLine.Split("\t")[0].Split(","), c: aLine.Split("\t")[2], b: aLine))
                .Select(x => (repo: x.a[1], number: int.Parse(x.a[2]), areaLabel: x.c, createdAt: x.a[0], itself: x.b))
                .ToArray();

            var result = new Dictionary<string, string>();
            var ignore = new Dictionary<string, string>();
            bool stopHere = false;
            bool anyLibOk = false; // make this true when u wanna skip what was incomplete last time
            foreach (var xx in iops)
            {
                if (xx.repo.Equals("corefx") && xx.number == 41089)
                {
                    anyLibOk = false;
                    // filtered already
                }
                if (anyLibOk)
                    continue;
                try
                {
                    (string, long, string, bool) canPick = 
                        await OctokitDownloadHelper.CanPickAsManuallyAssigned(xx.number, owner, xx.repo, xx.areaLabel, xx.createdAt);
                    if (canPick.Item4)
                    {
                        result.Add($"{canPick.Item1},{canPick.Item3},{canPick.Item2}", xx.itself);
                    }
                    else
                    {
                        ignore.Add($"{canPick.Item1},{canPick.Item3},{canPick.Item2}", xx.itself);
                    }
                }
                catch (Exception ex)
                {
                    stopHere = true;
                    Trace.WriteLine($"threw on {xx.repo} #{xx.number}" + ex.Message);
                    break;
                }
            }

            var newLines = new List<string>();
            foreach (var line in lines)
            {
                if (!string.IsNullOrEmpty(line) && line.Split('\t')[0].Split(',').Length == 3)
                {
                    var combinedId = line.Split('\t')[0];
                    if (result.ContainsKey(combinedId))
                    {
                        newLines.Add(result[combinedId]);
                    }
                    else if (ignore.ContainsKey(combinedId))
                    {
                        // ignore
                    }
                    else
                    {
                        // it's not an area of interest
                        newLines.Add(line);
                    }
                }
                else
                {
                    newLines.Add(line);
                }
            }
            Trace.WriteLine("finished? " + !stopHere);
            await File.WriteAllLinesAsync(outputFile, newLines);
        }
    }
}
