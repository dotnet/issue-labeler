// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Actions.Core.Extensions;
using Actions.Core.Markdown;
using Actions.Core.Services;
using IssueLabeler.Evaluation;
using IssueLabeler.Training;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.Text;
using static DataFileUtils;

using var provider = new ServiceCollection()
    .AddGitHubActionsCore()
    .BuildServiceProvider();

var action = provider.GetRequiredService<ICoreService>();

var config = Args.Parse(args, action);
if (config is not Args argsData) return 1;

List<Task> tasks = new();

if (argsData.IssuesDataPath is not null && argsData.IssuesModelPath is not null)
{
    tasks.Add(Task.Run(() => CreateModel(argsData.IssuesDataPath, argsData.IssuesModelPath, ModelType.Issue, action)));
}

if (argsData.PullsDataPath is not null && argsData.PullsModelPath is not null)
{
    tasks.Add(Task.Run(() => CreateModel(argsData.PullsDataPath, argsData.PullsModelPath, ModelType.PullRequest, action)));
}

var success = await App.RunTasks(tasks, action);
return success ? 0 : 1;

static async Task CreateModel(string dataPath, string modelPath, ModelType type, ICoreService action)
{
    if (!File.Exists(dataPath))
    {
        action.WriteNotice($"The data file '{dataPath}' does not exist.");
        action.Summary.AddPersistent(summary => summary.AddAlert("The data file does not exist. Training cannot proceed.", AlertType.Caution));
        await action.Summary.WriteAsync();

        throw new InvalidOperationException($"The data file '{dataPath}' does not exist.");
    }

    int recordsCounted = File.ReadLines(dataPath).Take(10).Count();
    if (recordsCounted < 10)
    {
        action.WriteNotice($"The data file '{dataPath}' does not contain enough data for training. A minimum of 10 records is required, but only {recordsCounted} exist.");
        action.Summary.AddPersistent(summary => summary.AddAlert($"Only {recordsCounted} items were found to be used for training. A minimum of 10 records is required. Cannot proceed with training.", AlertType.Caution));
        await action.Summary.WriteAsync();

        throw new InvalidOperationException($"The data file '{dataPath}' does not contain enough data for training. A minimum of 10 records is required, but only {recordsCounted} exist.");
    }

    string datasetSha256 = EvaluationArtifact.HashFile(dataPath);
    await action.WriteStatusAsync("Loading data into train/test sets...");
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
    var trainingData = split.TrainSet;
    var heldOutData = split.TestSet;

    await action.WriteStatusAsync("Building pipeline...");

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

    await action.WriteStatusAsync("Fitting the model with the training data set...");
    var trainedModel = pipeline.Fit(trainingData);
    var testModel = trainedModel.Transform(heldOutData);

    await action.WriteStatusAsync("Evaluating against the test set...");
    var metrics = mlContext.MulticlassClassification.Evaluate(testModel, labelColumnName: "LabelKey");

    action.Summary.AddPersistent(summary =>
    {
        summary.AddMarkdownHeading($"Finished Training {(type == ModelType.Issue ? "Issues" : "Pull Requests")} Model", 2);

        summary.AddRawMarkdown($"""
            * MacroAccuracy: {metrics.MacroAccuracy:0.####} (a value between 0 and 1; the closer to 1, the better)
            * MicroAccuracy: {metrics.MicroAccuracy:0.####} (a value between 0 and 1; the closer to 1, the better)
            * LogLoss: {metrics.LogLoss:0.####} (the closer to 0, the better)
            {(metrics.PerClassLogLoss.Count() > 0 ? $"    * Class 1: {metrics.PerClassLogLoss[0]:0.####}" : "")}
            {(metrics.PerClassLogLoss.Count() > 1 ? $"    * Class 2: {metrics.PerClassLogLoss[1]:0.####}" : "")}
            {(metrics.PerClassLogLoss.Count() > 2 ? $"    * Class 3: {metrics.PerClassLogLoss[2]:0.####}" : "")}
            """);
    });

    await action.Summary.WriteAsync();

    action.WriteInfo($"************************************************************");
    action.WriteInfo($"MacroAccuracy = {metrics.MacroAccuracy:0.####}, a value between 0 and 1, the closer to 1, the better");
    action.WriteInfo($"MicroAccuracy = {metrics.MicroAccuracy:0.####}, a value between 0 and 1, the closer to 1, the better");
    action.WriteInfo($"LogLoss = {metrics.LogLoss:0.####}, the closer to 0, the better");

    if (metrics.PerClassLogLoss.Count() > 0)
        action.WriteInfo($"LogLoss for class 1 = {metrics.PerClassLogLoss[0]:0.####}, the closer to 0, the better");

    if (metrics.PerClassLogLoss.Count() > 1)
        action.WriteInfo($"LogLoss for class 2 = {metrics.PerClassLogLoss[1]:0.####}, the closer to 0, the better");

    if (metrics.PerClassLogLoss.Count() > 2)
        action.WriteInfo($"LogLoss for class 3 = {metrics.PerClassLogLoss[2]:0.####}, the closer to 0, the better");

    action.WriteInfo($"************************************************************");

    action.WriteInfo($"Saving model to '{modelPath}'...");
    EnsureOutputDirectory(modelPath);
    mlContext.Model.Save(trainedModel, split.TrainSet.Schema, modelPath);

    var evaluation = HeldOutEvaluation.Save(mlContext, trainingData, heldOutData, type, dataPath, modelPath, datasetSha256);
    string evaluationPath = EvaluationArtifact.GetPath(modelPath);
    action.WriteInfo($"Saved {evaluation.Rows.Length} held-out predictions to '{evaluationPath}'.");
    action.Summary.AddPersistent(summary =>
    {
        summary.AddAlert(
            $"Saved **{evaluation.Rows.Length:N0}** label-free held-out predictions alongside the model. Evaluate thresholds from these saved scores without retraining or redownloading issues. Once used for threshold selection, this holdout is a tuning set, not an untouched final test.",
            AlertType.Note);
    });
    await action.Summary.WritePersistentAsync();
}
