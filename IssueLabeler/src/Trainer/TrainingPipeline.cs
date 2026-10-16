// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ML;
using Microsoft.ML.Transforms.Text;

namespace Trainer;

internal static class TrainingPipeline
{
    internal static IEstimator<ITransformer> Create(MLContext mlContext, ModelType type)
    {
        string[] featureColumns = type == ModelType.Issue
            ? ["Title", "Body"]
            : ["Title", "Body", "FileNames", "FolderNames"];

        var xf = mlContext.Transforms;
        var pipeline = xf.Conversion.MapValueToKey(inputColumnName: "Label", outputColumnName: "LabelKey")
            .Append(xf.Text.FeaturizeText(
                "Features",
                new TextFeaturizingEstimator.Options(),
                featureColumns))
            .AppendCacheCheckpoint(mlContext)
            .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("LabelKey"))
            .Append(xf.Conversion.MapKeyToValue("PredictedLabel"));

        return pipeline;
    }
}
