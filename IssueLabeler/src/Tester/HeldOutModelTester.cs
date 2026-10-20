// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ML;
using Microsoft.ML.Data;

public static class HeldOutModelTester
{
    public static IEnumerable<(string? Label, string? PredictedLabel, float? Score)> Predict(string modelPath, float threshold)
    {
        string testDataPath = Path.ChangeExtension(modelPath, ".test-data.bin");
        if (!File.Exists(testDataPath))
        {
            throw new FileNotFoundException(
                "The saved held-out dataset is missing. Retrain the model to create it; live GitHub data cannot replace it.",
                testDataPath);
        }

        var context = new MLContext();
        var data = context.Data.LoadFromBinary(testDataPath);
        using var dataLifetime = data as IDisposable;
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
            throw new InvalidOperationException("The saved held-out dataset is empty. Retrain the model with more data.");
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
