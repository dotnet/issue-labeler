// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using static DataFileUtils;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.Text;

var config = Args.Parse(args);
if (config is not Args argsData)
{
    return;
}

if (argsData.IssueDataPath is not null && argsData.IssueModelPath is not null)
{
    CreateModel(argsData.IssueDataPath, argsData.IssueModelPath, ModelType.Issue);
}

if (argsData.PullDataPath is not null && argsData.PullModelPath is not null)
{
    CreateModel(argsData.PullDataPath, argsData.PullModelPath, ModelType.PullRequest);
}

static void CreateModel(string dataPath, string modelPath, ModelType type)
{
    Console.WriteLine("Loading data into train/test sets...");
    MLContext mlContext = new();

    TextLoader.Column[] columns = type == ModelType.Issue ? [
        new("Label", DataKind.String, 0),
        new("Title", DataKind.String, 1),
        new("Body", DataKind.String, 2),
    ] : [
        new("Label", DataKind.String, 0),
        new("Title", DataKind.String, 1),
        new("Body", DataKind.String, 2),
        new("FileNames", DataKind.String, 3),
        new("FolderNames", DataKind.String, 4)
    ];

    TextLoader.Options textLoaderOptions = new()
    {
        AllowQuoting = false,
        AllowSparse = false,
        EscapeChar = '"',
        HasHeader = true,
        ReadMultilines = false,
        Separators = ['\t'],
        TrimWhitespace = true,
        UseThreads = true,
        Columns = columns
    };

    var loader = mlContext.Data.CreateTextLoader(textLoaderOptions);
    var data = loader.Load(dataPath);
    var split = mlContext.Data.TrainTestSplit(data, testFraction: 0.2);

    Console.WriteLine("Building pipeline...");

    var xf = mlContext.Transforms;
    var pipeline = xf.Conversion.MapValueToKey(inputColumnName: "Label", outputColumnName: "LabelKey")
        .Append(xf.Text.FeaturizeText(
            "Features",
            new TextFeaturizingEstimator.Options(),
            columns.Select(c => c.Name).ToArray()))
        .AppendCacheCheckpoint(mlContext)
        .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("LabelKey"))
        .Append(xf.Conversion.MapKeyToValue("PredictedLabel"));

    Console.WriteLine("Fitting the model with the training data set...");
    var trainedModel = pipeline.Fit(split.TrainSet);
    var testModel = trainedModel.Transform(split.TestSet);

    Console.WriteLine("Evaluating against the test set...");
    var metrics = mlContext.MulticlassClassification.Evaluate(testModel, labelColumnName: "LabelKey");

    Console.WriteLine($"************************************************************");
    Console.WriteLine($"MacroAccuracy = {metrics.MacroAccuracy:0.####}, a value between 0 and 1, the closer to 1, the better");
    Console.WriteLine($"MicroAccuracy = {metrics.MicroAccuracy:0.####}, a value between 0 and 1, the closer to 1, the better");
    Console.WriteLine($"LogLoss = {metrics.LogLoss:0.####}, the closer to 0, the better");

    if (metrics.PerClassLogLoss.Count() > 0)
        Console.WriteLine($"LogLoss for class 1 = {metrics.PerClassLogLoss[0]:0.####}, the closer to 0, the better");

    if (metrics.PerClassLogLoss.Count() > 1)
        Console.WriteLine($"LogLoss for class 2 = {metrics.PerClassLogLoss[1]:0.####}, the closer to 0, the better");

    if (metrics.PerClassLogLoss.Count() > 2)
        Console.WriteLine($"LogLoss for class 3 = {metrics.PerClassLogLoss[2]:0.####}, the closer to 0, the better");

    Console.WriteLine($"************************************************************");

    Console.WriteLine($"Saving model to '{modelPath}'...");
    EnsureOutputDirectory(modelPath);
    mlContext.Model.Save(trainedModel, split.TrainSet.Schema, modelPath);
}
