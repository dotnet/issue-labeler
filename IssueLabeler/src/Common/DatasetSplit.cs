// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class DatasetSplit
{
    private const string ModelReferenceEntry = "DatasetSplit.sha256";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true
    };
    private string fingerprint = string.Empty;

    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
    public required string DatasetSha256 { get; init; }
    [JsonRequired]
    public int ColumnCount { get; init; }
    [JsonRequired]
    public int RowCount { get; init; }
    public required int[] TestRows { get; init; }

    public static string GetPath(string dataPath) => Path.ChangeExtension(dataPath, ".split.json");

    public static DatasetSplit Create(string dataPath)
    {
        int columnCount = ReadHeader(dataPath).Length;
        int rowCount = ReadRecords(dataPath).Count();
        int testCount = rowCount < 2 ? 0 : Math.Clamp((int)Math.Round(rowCount * 0.2), 1, rowCount - 1);
        int[] indices = [.. Enumerable.Range(0, rowCount)];
        new Random(42).Shuffle(indices);
        var split = new DatasetSplit
        {
            DatasetSha256 = HashFile(dataPath),
            ColumnCount = columnCount,
            RowCount = rowCount,
            TestRows = [.. indices.Take(testCount).Order()]
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(split, JsonOptions);
        File.WriteAllBytes(GetPath(dataPath), bytes);
        split.fingerprint = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return split;
    }

    public static DatasetSplit Load(string dataPath)
    {
        if (!File.Exists(dataPath) || !File.Exists(GetPath(dataPath)))
        {
            throw new FileNotFoundException(
                "The original dataset or its split manifest is unavailable. Download data with a split manifest before training; evaluation requires the original cached dataset.",
                dataPath);
        }

        byte[] bytes = File.ReadAllBytes(GetPath(dataPath));
        var split = JsonSerializer.Deserialize<DatasetSplit>(bytes, JsonOptions)
            ?? throw new InvalidDataException("The dataset split manifest is empty.");
        if (split.SchemaVersion != 1 || split.ColumnCount is not (3 or 5) || split.RowCount < 0 ||
            split.TestRows is null || (split.RowCount > 0 && split.TestRows.Length >= split.RowCount) ||
            split.TestRows.Where((row, index) => row < 0 || row >= split.RowCount ||
                (index > 0 && row <= split.TestRows[index - 1])).Any())
        {
            throw new InvalidDataException("The dataset split manifest has invalid schema or row membership.");
        }

        if (!HashFile(dataPath).Equals(split.DatasetSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The dataset does not match its split manifest. Restore the original dataset; do not regenerate the split for evaluation.");
        }

        if (ReadHeader(dataPath).Length != split.ColumnCount)
        {
            throw new InvalidDataException("The dataset columns do not match its split manifest.");
        }

        split.fingerprint = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return split;
    }

    public IEnumerable<DatasetRow> ReadRows(string dataPath, bool test)
    {
        var testRows = new HashSet<int>(TestRows);
        int index = 0;
        foreach (string[] fields in ReadRecords(dataPath))
        {
            if (testRows.Contains(index++) == test)
            {
                DatasetRow row = ColumnCount == 3 ? new DatasetRow() : new PullRequestDatasetRow
                {
                    FileNames = fields[3],
                    FolderNames = fields[4]
                };
                row.Label = fields[0];
                row.Title = fields[1];
                row.Body = fields[2];
                yield return row;
            }
        }

        if (index != RowCount)
        {
            throw new InvalidDataException("The dataset row count does not match its split manifest.");
        }
    }

    public void WriteModelReference(string modelPath)
    {
        using var archive = ZipFile.Open(modelPath, ZipArchiveMode.Update);
        archive.GetEntry(ModelReferenceEntry)?.Delete();
        using var writer = new StreamWriter(archive.CreateEntry(ModelReferenceEntry).Open(), Encoding.ASCII);
        writer.Write(fingerprint);
    }

    public void VerifyModelReference(string modelPath)
    {
        using var archive = ZipFile.OpenRead(modelPath);
        var entry = archive.GetEntry(ModelReferenceEntry)
            ?? throw new InvalidDataException("The model has no dataset split reference. Retrain it using a dataset with a split manifest.");
        using var reader = new StreamReader(entry.Open());
        if (!fingerprint.Equals(reader.ReadToEnd(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The model was trained with a different dataset or split. Restore the original dataset and manifest.");
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string[] ReadHeader(string path)
    {
        string[] fields = File.ReadLines(path).FirstOrDefault()?.Split('\t')
            ?? throw new InvalidDataException("The dataset has no header.");
        string[] expected = fields.Length == 3
            ? ["Label", "Title", "Body"]
            : ["Label", "Title", "Body", "FileNames", "FolderNames"];
        if (!fields.SequenceEqual(expected))
        {
            throw new InvalidDataException("The dataset has an invalid column header.");
        }

        return fields;
    }

    private static IEnumerable<string[]> ReadRecords(string path)
    {
        int columnCount = ReadHeader(path).Length;
        foreach (string line in File.ReadLines(path).Skip(1))
        {
            string[] fields = line.Split('\t');
            if (fields.Length != columnCount)
            {
                throw new InvalidDataException("A dataset row has an invalid column count.");
            }

            // Match TextLoader's whole-line trailing whitespace trim, not a trim of every field.
            for (int last = fields.Length - 1; last >= 0; last--)
            {
                fields[last] = fields[last].TrimEnd();
                if (fields[last].Length > 0)
                {
                    break;
                }
            }

            yield return [.. fields.Select(field => field.TrimStart(' '))];
        }
    }
}

public class DatasetRow
{
    public string Label { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public sealed class PullRequestDatasetRow : DatasetRow
{
    public string FileNames { get; set; } = string.Empty;
    public string FolderNames { get; set; } = string.Empty;
}
