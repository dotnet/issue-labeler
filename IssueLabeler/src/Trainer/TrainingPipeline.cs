// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ML;
using Microsoft.ML.Trainers;
using Microsoft.ML.Transforms.Text;

internal static class TrainingPipeline
{
    internal static IEstimator<ITransformer> Create(MLContext mlContext, ModelType type)
    {
        var xf = mlContext.Transforms;
        return xf.Conversion.MapValueToKey(inputColumnName: "Label", outputColumnName: "LabelKey")
            .Append(CreateFeatures(mlContext, type))
            .AppendCacheCheckpoint(mlContext)
            .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(CreateTrainerOptions()))
            .Append(xf.Conversion.MapKeyToValue("PredictedLabel"));
    }

    internal static TextFeaturizingEstimator CreateFeatures(MLContext mlContext, ModelType type)
    {
        var options = new TextFeaturizingEstimator.Options();
        var characters = options.CharFeatureExtractor;
        characters.Weighting = NgramExtractingEstimator.WeightingCriteria.TfIdf;
        // Assign back because the setter snapshots the extractor configuration.
        options.CharFeatureExtractor = characters;

        string[] featureColumns = type == ModelType.Issue
            ? ["Title", "Body"]
            : ["Title", "Body", "FileNames", "FolderNames"];

        return mlContext.Transforms.Text.FeaturizeText("Features", options, featureColumns);
    }

    internal static SdcaMaximumEntropyMulticlassTrainer.Options CreateTrainerOptions()
        => new()
        {
            LabelColumnName = "LabelKey",
            L2Regularization = 3e-6f
        };
}
