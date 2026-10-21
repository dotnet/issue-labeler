// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Actions.Core.Extensions;
using Actions.Core.Markdown;
using Actions.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.Text;
using static DataFileUtils;

internal static class Trainer
{
    private static async Task<int> Main(string[] args)
    {
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
    }

    private static async Task CreateModel(string dataPath, string modelPath, ModelType type, ICoreService action)
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
        var trainedModel = pipeline.Fit(split.TrainSet);
        var testModel = trainedModel.Transform(split.TestSet);

        await action.WriteStatusAsync("Evaluating against the test set...");
        VBuffer<ReadOnlyMemory<char>> slots = default;
        testModel.Schema[nameof(HeldOutPrediction.Score)].GetSlotNames(ref slots);
        string[] labels = [.. slots.DenseValues().Select(label => label.ToString())];
        var evaluation = Evaluate(
            mlContext.Data.CreateEnumerable<HeldOutPrediction>(testModel, reuseRowObject: false), labels);
        var metrics = mlContext.MulticlassClassification.Evaluate(testModel, labelColumnName: "LabelKey");
        var classLosses = metrics.PerClassLogLoss.Take(3)
            .Select((loss, index) => FormattableString.Invariant($"| Class {index + 1} | {loss:0.####} |"));
        string metricsReport = FormattableString.Invariant($"""
            These metrics use all held-out items, without a score cutoff.

            | Metric | Value | Meaning |
            |---|---:|---|
            | MacroAccuracy | {metrics.MacroAccuracy:0.####} | Average accuracy across labels, each weighted equally (higher is better). |
            | MicroAccuracy | {metrics.MicroAccuracy:0.####} | Fraction of all items predicted correctly (higher is better). |
            | LogLoss | {metrics.LogLoss:0.####} | Penalizes low scores for the correct label (lower is better). |

            **Per-class LogLoss** (first three label categories, lower is better)

            | Class | LogLoss |
            |---|---:|
            {string.Join(Environment.NewLine, classLosses)}
            """);

        action.Summary.AddPersistent(summary =>
        {
            summary.AddMarkdownHeading($"Finished Training {(type == ModelType.Issue ? "Issues" : "Pull Requests")} Model", 2);

            summary.AddRawMarkdown(metricsReport);
            summary.AddNewLine();
            summary.AddNewLine();
            summary.AddRawMarkdown(evaluation.ToMarkdown(), true);
        });

        await action.Summary.WriteAsync();

        action.WriteInfo($"************************************************************");
        action.WriteInfo(metricsReport);
        action.WriteInfo($"************************************************************");
        action.WriteInfo(evaluation.ToMarkdown());

        action.WriteInfo($"Saving model to '{modelPath}'...");
        EnsureOutputDirectory(modelPath);
        mlContext.Model.Save(trainedModel, split.TrainSet.Schema, modelPath);
    }

    internal static HeldOutResult Evaluate(IEnumerable<HeldOutPrediction> predictions, string[] labels)
    {
        List<(float Score, bool Correct)> ranked = [];
        foreach (var prediction in predictions)
        {
            if (string.IsNullOrWhiteSpace(prediction.Label) || labels.Length == 0 || prediction.Score.Length != labels.Length ||
                prediction.Score.Any(score => !float.IsFinite(score)))
            {
                throw new InvalidDataException("A held-out row has an invalid reference label or score vector.");
            }

            int index = Array.IndexOf(prediction.Score, prediction.Score.Max());
            ranked.Add((prediction.Score[index], labels[index].Equals(prediction.Label, StringComparison.OrdinalIgnoreCase)));
        }

        if (ranked.Count == 0)
        {
            throw new InvalidOperationException("The held-out test set is empty. Download more data before training.");
        }

        int[] precisionTargets = [60, 70, 80, 90, 95];
        PrecisionTargetResult[] results = [.. precisionTargets.Select(target => new PrecisionTargetResult(target, null, 0, 0))];
        ranked.Sort((left, right) => right.Score.CompareTo(left.Score));
        int correct = 0;
        for (int index = 0; index < ranked.Count; index++)
        {
            if (ranked[index].Correct)
            {
                correct++;
            }

            // A cutoff must accept all predictions with the same score.
            if (index + 1 < ranked.Count && ranked[index].Score == ranked[index + 1].Score)
            {
                continue;
            }

            int accepted = index + 1;
            for (int targetIndex = 0; targetIndex < results.Length; targetIndex++)
            {
                int target = results[targetIndex].TargetPrecision;
                if ((long)correct * 100 >= (long)target * accepted)
                {
                    results[targetIndex] = new(target, ranked[index].Score, correct, accepted);
                }
            }
        }

        return new(ranked.Count, results);
    }
}

internal sealed class HeldOutPrediction
{
    public string Label { get; set; } = string.Empty;
    public float[] Score { get; set; } = [];
}

internal sealed record PrecisionTargetResult(int TargetPrecision, float? Threshold, int Correct, int Accepted)
{
    public double? Precision => Accepted == 0 ? null : (double)Correct / Accepted;
}

internal sealed record HeldOutResult(int Total, PrecisionTargetResult[] Targets)
{
    public string ToMarkdown()
    {
        var rows = Targets.Select(target => target.Threshold is float threshold
            ? FormattableString.Invariant($"| {target.TargetPrecision}% | {threshold:R} | {target.Precision:P2} | {(double)target.Accepted / Total:P2} | {target.Accepted} |")
            : FormattableString.Invariant($"| {target.TargetPrecision}% | Not reached | N/A | N/A | N/A |"));
        return FormattableString.Invariant($"""
            ### Held-out evaluation

            Evaluated {Total} items from the random 20% test split, excluded from model fitting.
            Each row aims to label as many test items as possible, with at least the target percentage labeled correctly.
            Threshold is the minimum score required to use a prediction. Accepted is the number of predictions kept at this cutoff, including incorrect predictions.

            | Target precision | Threshold | Observed precision | Coverage | Accepted |
            |---:|---:|---:|---:|---:|
            {string.Join(Environment.NewLine, rows)}

            Precision is the percentage of accepted predictions that are correct. Coverage is the percentage of all test items accepted.
            Thresholds are selected and evaluated on this same split.
            """);
    }
}
