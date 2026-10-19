// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using IssueLabeler.Evaluation;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace IssueLabeler.Training;

public static class HeldOutEvaluation
{
    public static EvaluationArtifact Save(
        MLContext context, IDataView trainingData, IDataView heldOutData,
        ModelType type, string datasetPath, string modelPath, string datasetSha256)
    {
        string modelSha256 = EvaluationArtifact.HashFile(modelPath);
        var model = context.Model.Load(modelPath, out _);
        using var predictor = context.Model.CreatePredictionEngine<PredictionInput, PredictionOutput>(model);
        VBuffer<ReadOnlyMemory<char>> slots = default;
        predictor.OutputSchema[nameof(PredictionOutput.Score)].GetSlotNames(ref slots);
        string[] labels = [.. slots.DenseValues().Select(value => value.ToString())];
        var trainingSupport = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in context.Data.CreateEnumerable<LabelValue>(trainingData, reuseRowObject: false))
        {
            trainingSupport[row.Label] = trainingSupport.GetValueOrDefault(row.Label) + 1;
        }

        var rows = new List<EvaluationRow>();
        foreach (var row in context.Data.CreateEnumerable<PredictionInput>(heldOutData, reuseRowObject: false, ignoreMissingColumns: true))
        {
            string goldLabel = row.Label ?? throw new InvalidDataException("A held-out row has no reference label.");
            row.Label = null;
            var prediction = predictor.Predict(row);
            rows.Add(new()
            {
                Index = rows.Count,
                Label = goldLabel,
                Scores = [.. prediction.Score]
            });
        }

        if (!datasetSha256.Equals(EvaluationArtifact.HashFile(datasetPath), StringComparison.OrdinalIgnoreCase) ||
            !modelSha256.Equals(EvaluationArtifact.HashFile(modelPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The dataset or model changed while collecting held-out predictions.");
        }

        var artifact = new EvaluationArtifact
        {
            ModelSha256 = modelSha256,
            DatasetSha256 = datasetSha256,
            ToolVersion = typeof(HeldOutEvaluation).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? throw new InvalidOperationException("The trainer assembly has no version information."),
            ModelType = type.ToString(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            TrainingCount = trainingSupport.Values.Sum(),
            TrainingSupport = trainingSupport,
            Labels = labels,
            Rows = [.. rows]
        };
        artifact.Save(EvaluationArtifact.GetPath(modelPath));
        return artifact;
    }

    private sealed class LabelValue
    {
        public string Label { get; set; } = string.Empty;
    }

    private sealed class PredictionInput
    {
        public string? Label { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? FileNames { get; set; }
        public string? FolderNames { get; set; }
    }

    private sealed class PredictionOutput
    {
        public float[] Score { get; set; } = [];
    }
}
