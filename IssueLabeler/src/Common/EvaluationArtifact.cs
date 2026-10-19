// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IssueLabeler.Evaluation;

public sealed class EvaluationArtifact
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
    public required string ModelSha256 { get; init; }
    public required string DatasetSha256 { get; init; }
    public required string ToolVersion { get; init; }
    public required string ModelType { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public int TrainingCount { get; init; }
    public required Dictionary<string, int> TrainingSupport { get; init; }
    public required string[] Labels { get; init; }
    public required EvaluationRow[] Rows { get; init; }

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true
    };

    public static string GetPath(string modelPath) => Path.ChangeExtension(modelPath, ".evaluation.json");

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static EvaluationArtifact Load(string path, string modelPath)
    {
        using var stream = File.OpenRead(path);
        var artifact = JsonSerializer.Deserialize<EvaluationArtifact>(stream, JsonOptions)
            ?? throw new InvalidDataException("The evaluation artifact is empty.");
        artifact.Validate();

        if (!artifact.ModelSha256.Equals(HashFile(modelPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The evaluation artifact does not match this model. Use the model and saved scores from the same training run.");
        }

        return artifact;
    }

    public void Save(string path)
    {
        Validate();
        DataFileUtils.EnsureOutputDirectory(path);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, this, JsonOptions);
    }

    public void Validate()
    {
        if (SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported evaluation schema version: {SchemaVersion}.");
        }

        if (!IsHash(ModelSha256) || !IsHash(DatasetSha256) || string.IsNullOrWhiteSpace(ToolVersion) ||
            ModelType is not ("Issue" or "PullRequest") || CreatedAtUtc == default)
        {
            throw new InvalidDataException("The evaluation artifact has invalid model, dataset, or tool provenance.");
        }

        if (TrainingSupport is null || TrainingCount <= 0 || TrainingSupport.Count == 0 ||
            TrainingSupport.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value <= 0) ||
            TrainingSupport.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != TrainingSupport.Count ||
            TrainingSupport.Values.Sum(value => (long)value) != TrainingCount)
        {
            throw new InvalidDataException("The evaluation artifact has invalid training support counts.");
        }

        if (Labels is null || Labels.Length == 0 || Labels.Length != TrainingSupport.Count || Labels.Any(string.IsNullOrWhiteSpace) ||
            Labels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Labels.Length ||
            Labels.Any(label => !TrainingSupport.Keys.Contains(label, StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The evaluation artifact has invalid score-slot labels.");
        }

        if (Rows is null || Rows.Length == 0)
        {
            throw new InvalidDataException("The evaluation artifact contains no held-out predictions.");
        }

        for (int index = 0; index < Rows.Length; index++)
        {
            var row = Rows[index];
            if (row is null || row.Index != index || string.IsNullOrWhiteSpace(row.Label) ||
                row.Scores is null || row.Scores.Length != Labels.Length ||
                row.Scores.Any(score => !float.IsFinite(score) || score < 0 || score > 1) ||
                Math.Abs(row.Scores.Sum(score => (double)score) - 1) > 0.01)
            {
                throw new InvalidDataException($"Invalid held-out prediction at row {index}.");
            }
        }
    }

    private static bool IsHash(string value) =>
        value is not null && value.Length == 64 && value.All(char.IsAsciiHexDigit);
}

public sealed class EvaluationRow
{
    [JsonRequired]
    public int Index { get; init; }
    public required string Label { get; init; }
    public required float[] Scores { get; init; }
}
