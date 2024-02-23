// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using Microsoft.Extensions.Configuration;

namespace ModelCreator.Downloader;

public static class CommonHelper
{
    public static void AddRange<T, S>(this Dictionary<T, S> source, Dictionary<T, S> collection)
        where T: notnull
    {
        ArgumentNullException.ThrowIfNull(nameof(collection));

        foreach (var item in collection)
        {
            if (!source.ContainsKey(item.Key))
            {
                source.Add(item.Key, item.Value);
            }
        }
    }

    public static readonly Action<List<string>, StreamWriter> action = (list, outputWriter) =>
    {
        var ordered = list
            .Where(x => !string.IsNullOrEmpty(x) && x.Split('\t')[0].Split(',').Length == 3)
            .Select(x => (x.Split('\t')[0].Split(','), x))
            .OrderBy(x => long.Parse(x.Item1[0]))   //-> first by created date
            .ThenBy(x => x.Item1[1])                //-> then by repo name
            .ThenBy(x => long.Parse(x.Item1[2]))    //-> then by issue number
            .Select(x => x.x);

        foreach (var item in ordered)
        {
            outputWriter.WriteLine(item);
        }
    };

    public static readonly Action<Dictionary<(DateTimeOffset, long, string), string>, StreamWriter>
        saveAction = (lookup, outputWriter) =>
        {
            var ordered = lookup
                .OrderBy(x => x.Key.Item1.UtcDateTime.ToFileTimeUtc())  //-> first by created date
                .ThenBy(x => x.Key.Item3)                               //-> then by repo name
                .ThenBy(x => x.Key.Item2)                               //-> then by issue number
                .Select(x => x.Value);

            foreach (var item in ordered)
            {
                outputWriter.WriteLine(item);
            }
        };

    public static string GetCompressedLine(
        IReadOnlyList<string> filePaths,
        string area,
        string? authorLogin,
        string prBody,
        string prTitle,
        DateTimeOffset prCreatedAt,
        int prNumber,
        string repo,
        bool isPr)
    {
        var author = authorLogin ?? GitHubHelpers.GitHubUser.DeletedUser;
        var body = (prBody ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Replace('"', '`');
        var title = prTitle.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Replace('"', '`');
        var createdAt = prCreatedAt.UtcDateTime.ToFileTimeUtc();
        if (isPr)
        {
            var filePathsJoined = string.Join(";", filePaths);
            return $"{createdAt},{repo},{prNumber}\t{prNumber}\t{area}\t{title}\t{body}\t{author}\t1\t{filePathsJoined}";
        }
        else
        {
            return $"{createdAt},{repo},{prNumber}\t{prNumber}\t{area}\t{title}\t{body}\t{author}\t0\t";
        }
    }

    public static void WriteCsvHeader(StreamWriter outputWriter)
    {
        outputWriter.WriteLine("CombinedID\tID\tArea\tTitle\tDescription\tAuthor\tIsPR\tFilePaths");
    }
}