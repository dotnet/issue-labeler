// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.DotNet.Github.IssueLabeler.Helpers
{
    public class DatasetHelper
    {
        public DatasetHelper(DiffHelper diffHelper)
        {
            _diffHelper = diffHelper;
            _sb = new StringBuilder();
            _folderSb = new StringBuilder();
            _regexForUserMentions = new Regex(@"@[a-zA-Z0-9_//-]+");
        }
        private readonly Regex _regexForUserMentions;
        private readonly StringBuilder _folderSb;
        private readonly DiffHelper _diffHelper;
        private readonly StringBuilder _sb;

        /// <summary>
        /// partitions the dataset in inputPath into train, validate and test datapaths
        /// </summary>
        /// <param name="inputPath">path to the input dataset</param>
        /// <param name="trainPath">the output to store the train dataset</param>
        /// <param name="validatePath">the output to store the train dataset</param>
        /// <param name="testPath">the output to store the train dataset</param>
        public void BreakIntoTrainValidateTestDatasets(string inputPath, string trainPath, string validatePath, string testPath)
        {
            var lines = File.ReadAllLines(inputPath);
            int totalCount = lines.Length;

            // have at least 1000 elements
            Debug.Assert(totalCount > 1000);
            int numInTrain = (int)(lines.Length * 0.8);
            int numInValidate = (int)(lines.Length * 0.1);

            // 80% into train dataset
            SaveFromXToY(
                inputPath,
                trainPath,
                numToSkip: 0, length: numInTrain); 

            // next 10% into validate dataset
            SaveFromXToY(
                inputPath,
                validatePath,
                numToSkip: numInTrain, length: numInValidate); // next 10%

            // remaining 10% into test dataset
            SaveFromXToY(
                inputPath,
                testPath,
                numToSkip: numInTrain + numInValidate);
        }

        private void SaveFromXToY(string input, string output, int numToSkip, int length = -1)
        {
            var lines = File.ReadAllLines(input);
            var span = lines.AsSpan();
            var header = span.Slice(0, 1).ToArray(); // include header
            File.WriteAllLines(output, header);
            span = span.Slice(numToSkip + 1, span.Length - (numToSkip + 1));
            if (length != -1)
            {
                span = span.Slice(0, length); // include header
            }
            lines = span.ToArray();
            File.AppendAllLines(output, lines);
        }

        /// <summary>
        /// saves to file a subset containing only PRs
        /// </summary>
        /// <param name="input">path to the reference dataset</param>
        /// <param name="output">the output to store the new dataset</param>
        public void OnlyPrs(string input, string output)
        {
            var lines = File.ReadAllLines(input);
            var span = lines.AsSpan();
            var header = span.Slice(0, 1).ToArray(); // include header
            Debug.Assert(header[0].Split("\t")[3] == "IsPR");
            File.WriteAllLines(output, header);
            span = span.Slice(1, span.Length - 1);
            lines = span.ToArray();
            File.AppendAllLines(output, lines.Where(x => int.TryParse(x.Split('\t')[3], out int isPrAsNumber) && isPrAsNumber == 1).ToArray());
        }

        /// <summary>
        /// saves to file a subset containing only issues
        /// </summary>
        /// <param name="input">path to the reference dataset</param>
        /// <param name="output">the output to store the new dataset</param>
        public void OnlyIssues(string input, string output)
        {
            var lines = File.ReadAllLines(input);
            var span = lines.AsSpan();
            var header = span.Slice(0, 1).ToArray(); // include header
            Debug.Assert(header[0].Split("\t")[3] == "IsPR");
            File.WriteAllLines(output, header);
            span = span.Slice(1, span.Length - 1);
            lines = span.ToArray();
            File.AppendAllLines(output, lines.Where(x => int.TryParse(x.Split('\t')[3], out int isPrAsNumber) && isPrAsNumber == 0).ToArray());
        }

        /// <summary>
        /// saves to file a dataset ready for training, given one created using GithubIssueDownloader.
        /// For training we can remove ID column, and further expand information in FilePaths
        /// We also retrieve user @ mentions from instead Description and add into new columns
        /// </summary>
        /// <param name="input">path to the reference dataset</param>
        /// <param name="output">the output to store the new dataset</param>
        /// <param name="includeFileColumns">when true, it contains extra columns with file related information</param>
        public void AddOrRemoveColumnsPriorToTraining(string input, string output, bool skipUnknownAreas = true, bool includeFileColumns = true)
        {
            var lines = File.ReadAllLines(input);
            string curHeader = 
                "CombinedID\tID\tArea\tTitle\tDescription\tIsPR\tFilePaths\t" + 
                "CreatedAt\tHtmlUrl\tNumComments\tAllLabels\tMilestone\t" + 
                "IssueAuthor\tIssueAssignee\tIssueAssignees\tIssueCloser\t" + 
                "PrAuthor\tPrAssignee\tPrAssignees\tPrMerger\tPrReviewers\t" + 
                "CommitCommenters\tCommitComments\tCommenters\tComments";
            // "ID\tArea\tTitle\tDescription\tIsPR\tFilePaths"

            var headerIndices = new Dictionary<string, int>();
            var headerNames = curHeader.Split('\t');
            for (int i = 0; i < headerNames.Length; i++)
            {
                headerIndices.Add(headerNames[i], i);
            }

            var headersToSkip = new string[] { "CombinedID", "ID", "CreatedAt", "HtmlUrl", "AllLabels", "NumComments", "Milestone"
                , "CommentsCombined"
            };
            var indicesToKeepAsIs = new string[] { "Area", "Title", "Description", "IsPR", "IssueAuthor" };
            var newOnesToAdd = new string[] { "NumMentions", "UserMentions" };

            var newHeader = "";
            var sbInner = new StringBuilder();
            foreach (var item in indicesToKeepAsIs.Union(newOnesToAdd.SkipLast(1)))
            {
                sbInner.Append(item).Append("\t");
            }
            sbInner.Append(newOnesToAdd.Last());
            if (includeFileColumns)
            {
                sbInner.Append("\tFileCount\tFiles\tFilenames\tFileExtensions\tFolderNames\tFolders");
            }
            newHeader = sbInner.ToString();

            var indicesWithFuncToChange = new string[] { 
                "IssueAssignee", "IssueAssignees", "IssueCloser", 
                "PrMerger", "PrAuthor", "PrAssignee", "PrAssignees", "PrReviewers", 
                "CommitCommenters", "CommitComments", 
                "Commenters", "Comments" 
            };
            //Debug.Assert(headerIndices.Count == headersToSkip.Length + indicesToKeepAsIs.Length + indicesWithFuncToChange.Length + 1);
            string body, area;
            var newLines = new List<string>();
            newLines.Add(newHeader);
            if (lines.Length != 0)
            {
                foreach (var line in lines.Where(x => !x.StartsWith("CombinedID") && !string.IsNullOrEmpty(x)))
                {
                    _sb.Clear();
                    var lineSplitByTab = line.Split("\t");
                    //Prevalidate(headerIndices, lineSplitByTab);
                    if (skipUnknownAreas && string.IsNullOrEmpty(lineSplitByTab[headerIndices["Area"]]))
                    {
                        continue;
                    }

                    Debug.Assert(int.TryParse(lineSplitByTab[headerIndices["ID"]], out int _)); // skip ID
                    body = lineSplitByTab[headerIndices["Description"]];
                    int.TryParse(lineSplitByTab[headerIndices["IsPR"]], out int isPrAsNumber);
                    Debug.Assert((isPrAsNumber == 1 || isPrAsNumber == 0));

                    area = lineSplitByTab[headerIndices["Area"]].Equals("area-Build", StringComparison.OrdinalIgnoreCase) ? "area-Infrastructure-coreclr" : lineSplitByTab[headerIndices["Area"]];
                    _sb.Append(area)
                        .Append('\t').Append(lineSplitByTab[headerIndices["Title"]])
                        .Append('\t').Append(body)
                        .Append('\t').Append(isPrAsNumber);
                    _sb.Append('\t').Append(lineSplitByTab[headerIndices["IssueAuthor"]]);

                    string commentsCombined = CommentsCombined(headerIndices, lineSplitByTab, out string moreMentions);
                    AppendColumnsForUserMentions(body + " " + commentsCombined, moreMentions);
                    //_sb.Append('\t').Append(commentsCombined);
                    if (includeFileColumns)
                    {
                        AppendColumnsForFileDiffs(lineSplitByTab[headerIndices["FilePaths"]], isPr: isPrAsNumber == 1, repoFrom: lineSplitByTab[0].Split(",")[1]);
                    }
                    newLines.Add(_sb.ToString().Replace('"', '`'));
                }
            }

            File.WriteAllLines(output, newLines);
        }
        private readonly StringBuilder _commentsSb = new StringBuilder();
        private readonly StringBuilder _mentionsSb = new StringBuilder();


        private string CommentsCombined(Dictionary<string, int> headerIndices, string[] splits, out string moreMentions)
        {
            moreMentions = string.Empty;
            _commentsSb.Clear();
            _mentionsSb.Clear();
            for (int i = 12; i < splits.Length; i++)
            {
                if (!string.IsNullOrEmpty(splits[i]))
                {
                    if (splits[i].Contains(" "))
                    {
                        if (_commentsSb.Length > 0)
                        {
                            _commentsSb.Append(". ");
                        }
                        _commentsSb.Append(splits[i]);
                    }
                    else
                    {
                        if (_mentionsSb.Length > 0)
                        {
                            _mentionsSb.Append(";");
                        }
                        _mentionsSb.Append(splits[i]);
                    }
                }
            }
            moreMentions = _mentionsSb.ToString();
            return _commentsSb.ToString();
        }

        private void AppendColumnsForUserMentions(string body, string moreMentions)
        {
            var userMentions = _regexForUserMentions.Matches(body).Select(x => x.Value).ToArray();
            userMentions = userMentions.Union(moreMentions.Split(";").Select(x => "@" + x)).ToArray();
            _sb.Append('\t').Append(userMentions.Length)
                .Append('\t').Append(FlattenIntoColumn(userMentions));
        }

        private void AppendColumnsForFileDiffs(string semicolonDelimitedFilesWithDiff, bool isPr, string repoFrom, bool alreadyFixed = true)
        {
            if (isPr)
            {
                string[] filePaths = semicolonDelimitedFilesWithDiff.Split(';');
                int numFilesChanged = filePaths.Length == 1 && string.IsNullOrEmpty(filePaths[0]) ? 0 : filePaths.Length;
                _sb.Append('\t').Append(numFilesChanged);
                if (numFilesChanged != 0)
                {
                    if (!alreadyFixed)
                    {
                        for (int i = 0; i < filePaths.Length; i++)
                        {
                            if (filePaths[i].StartsWith($"src/coreclr/"))
                            {
                                filePaths[i] = $"src/coreclr/src/" + filePaths[i].Substring(
                                    $"src/coreclr/".Length);
                            }
                            if (filePaths[i].Contains($"src/coreclr/src/mscorlib/shared/"))
                            {
                                filePaths[i] = filePaths[i].Replace(
                                    $"src/coreclr/src/mscorlib/shared/",
                                    $"src/libraries/System.Private.CoreLib/src/");
                            }
                            if (filePaths[i].Contains($"src/coreclr/System.Private.CoreLib/shared"))
                            {
                                filePaths[i] = filePaths[i].Replace(
                                    $"src/coreclr/src/System.Private.CoreLib/shared/",
                                    $"src/libraries/System.Private.CoreLib/src/");
                            }
                            else if (filePaths[i].Contains($".azure-ci.yml"))
                            {
                                filePaths[i] = filePaths[i].Replace(
                                    $".azure-ci.yml",
                                    $"eng/pipelines/" + repoFrom + $"/.azure-ci.yml");
                            }
                            else if (filePaths[i].Contains($"azure-pipelines.yml"))
                            {
                                filePaths[i] = filePaths[i].Replace(
                                    $"azure-pipelines.yml",
                                    $"eng/pipelines/" + repoFrom + $"/azure-pipelines.yml");
                            }
                            else if (filePaths[i].Contains($"eng/pipelines/"))
                            {
                                filePaths[i] = filePaths[i].Replace(
                                    $"eng/pipelines",
                                    $"eng/pipelines/" + repoFrom);
                            }
                        }
                    }
                    var segmentedDiff = _diffHelper.SegmentDiff(filePaths);
                    _sb.Append('\t').Append(FlattenIntoColumn(filePaths))
                        .Append('\t').Append(FlattenIntoColumn(segmentedDiff.filenames))
                        .Append('\t').Append(FlattenIntoColumn(segmentedDiff.extensions))
                        .Append('\t').Append(FlattenIntoColumn(segmentedDiff.folderNames))
                        .Append('\t').Append(FlattenIntoColumn(segmentedDiff.folders));
                }
                else
                {
                    _sb.Append('\t', 5);
                }
            }
            else
            {
                _sb.Append('\t').Append(0)
                    .Append('\t', 5);
            }
        }

        /// <summary>
        /// flattens a dictionary to be repeated in a space separated format
        /// </summary>
        /// <param name="textToFlatten">a dictionary containing text and number of times they were repeated</param>
        /// <returns>space delimited text</returns>
        public string FlattenIntoColumn(Dictionary<string, int> folder)
        {
            _folderSb.Clear();
            string res;
            foreach (var f in folder.OrderByDescending(x => x.Value))
            {
                Debug.Assert(f.Value >= 1);
                _folderSb.Append(f.Key);
                for (int j = 0; j < f.Value - 1; j++)
                {
                    _folderSb.Append(" ").Append(f.Key);
                }
                _folderSb.Append(" ");
            }
            if (_folderSb.Length == 0)
            {
                res = string.Empty;
            }
            else
            {
                res = _folderSb.ToString();
                res = res.Substring(0, res.Length - 1);
            }
            return res;
        }

        /// <summary>
        /// flattens texts in a space separated format
        /// </summary>
        /// <param name="array">the input containing text to show</param>
        /// <returns>space delimited text</returns>
        public string FlattenIntoColumn(string[] array)
        {
            return string.Join(' ', array);
        }

        /// <summary>
        /// flattens texts in a space separated format
        /// </summary>
        /// <param name="enumerable">the input containing text to show</param>
        /// <returns>space delimited text</returns>
        public string FlattenIntoColumn(IEnumerable<string> enumerable)
        {
            return string.Join(' ', enumerable);
        }
    }
}
