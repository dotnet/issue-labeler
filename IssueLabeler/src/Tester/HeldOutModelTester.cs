// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ML;
using Microsoft.ML.Data;

public static class HeldOutModelTester
{
    public static IEnumerable<(string? Label, string? PredictedLabel, float? Score)> Predict(
        string modelPath, string dataPath, float threshold, ModelType type = ModelType.Issue)
    {
        var split = DatasetSplit.Load(dataPath);
        split.VerifyModelReference(modelPath);
        if (split.ColumnCount != (type == ModelType.Issue ? 3 : 5))
        {
            throw new InvalidDataException("The dataset columns do not match the requested model type.");
        }
        if (split.TestRows.Length == 0)
        {
            throw new InvalidOperationException("The dataset has no held-out rows. Download more data before training.");
        }

        var context = new MLContext();
        var rows = split.ReadRows(dataPath, test: true).ToArray();
        var data = type == ModelType.Issue
            ? context.Data.LoadFromEnumerable(rows)
            : context.Data.LoadFromEnumerable(rows.Cast<PullRequestDatasetRow>());
        var model = context.Model.Load(modelPath, out _);
        var predictions = model.Transform(data);

        VBuffer<ReadOnlyMemory<char>> labelNames = default;
        predictions.Schema[nameof(LabelPrediction.Score)].GetSlotNames(ref labelNames);
        string[] labels = labelNames.DenseValues().Select(label => label.ToString()).ToArray();
        bool hasRows = false;

        foreach (var prediction in context.Data.CreateEnumerable<LabelPrediction>(predictions, reuseRowObject: false))
        {
            hasRows = true;
            var (predictedLabel, score) = SelectPrediction(prediction.Score, labels, threshold);
            yield return (string.IsNullOrEmpty(prediction.Label) ? null : prediction.Label, predictedLabel, score);
        }

        if (!hasRows)
        {
            throw new InvalidOperationException("The dataset has no held-out rows. Download more data before training.");
        }
    }

    internal static (string? Label, float? Score) SelectPrediction(float[] scores, string[] labels, float threshold)
    {
        if (scores.Length != labels.Length)
        {
            throw new InvalidOperationException("The model's score and label counts do not match.");
        }

        var best = scores
            .Select((score, index) => (Score: score, Label: labels[index]))
            .OrderByDescending(prediction => prediction.Score)
            .FirstOrDefault(prediction => prediction.Score >= threshold);

        return best.Label is not null ? (best.Label, best.Score) : (null, null);
    }
}
