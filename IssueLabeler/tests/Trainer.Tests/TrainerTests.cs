// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Globalization;
using Microsoft.ML;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace IssueLabeler.Tests;

[TestClass]
public sealed class TrainerTests
{
    private string directory = string.Empty;

    [TestInitialize]
    public void Initialize() => directory = Directory.CreateTempSubdirectory("issue-labeler-training-").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(directory, recursive: true);

    [TestMethod]
    public void PrecisionTargetsChooseMaximumCoverageEvenWhenPrecisionRecovers()
    {
        int[] wrongRanks = [2, 11, 12, 16, 17, 18, 21, 22, 23, 24];
        HeldOutPrediction[] predictions = [.. Enumerable.Range(1, 25).Reverse().Select(rank => new HeldOutPrediction
        {
            Label = wrongRanks.Contains(rank) ? "area-B" : "area-A",
            Score = [(100 - rank) / 100f, rank / 100f]
        })];
        var result = HeldOutResult.Evaluate(predictions, ["area-A", "area-B"]);

        Assert.AreEqual(25, result.Total);
        Assert.HasCount(56, result.Targets);
        Assert.AreEqual(new PrecisionTargetResult(40, 0.75f, 15, 25), result.Targets[0]);
        Assert.AreEqual(new PrecisionTargetResult(50, 0.75f, 15, 25), result.Targets[10]);
        Assert.AreEqual(new PrecisionTargetResult(60, 0.75f, 15, 25), result.Targets[20]);
        Assert.AreEqual(new PrecisionTargetResult(70, 0.8f, 14, 20), result.Targets[30]);
        Assert.AreEqual(new PrecisionTargetResult(80, 0.85f, 12, 15), result.Targets[40]);
        Assert.AreEqual(new PrecisionTargetResult(85, 0.9f, 9, 10), result.Targets[45]);
        Assert.AreEqual(new PrecisionTargetResult(90, 0.9f, 9, 10), result.Targets[50]);
        Assert.AreEqual(new PrecisionTargetResult(95, 0.99f, 1, 1), result.Targets[55]);
        Assert.AreEqual(0.6, result.Targets[0].Precision);
    }

    [TestMethod]
    public void TiedPredictionScoresAreAcceptedTogetherAtTheThreshold()
    {
        var result = HeldOutResult.Evaluate(
            [new() { Label = "AREA-A", Score = [0.9f, 0.1f] },
             new() { Label = "area-B", Score = [0.9f, 0.1f] },
             new() { Label = "area-A", Score = [0.8f, 0.2f] }],
            ["area-A", "area-B"]);
        Assert.AreEqual(new PrecisionTargetResult(40, 0.8f, 2, 3), result.Targets[0]);
        Assert.AreEqual(new PrecisionTargetResult(41, 0.8f, 2, 3), result.Targets[1]);
        Assert.AreEqual(new PrecisionTargetResult(66, 0.8f, 2, 3), result.Targets[26]);
        Assert.IsNull(result.Targets[27].Threshold);
    }

    [TestMethod]
    public void UnreachableTargetsAreNotReportedAsPerfectPrecisionWithNoCoverage()
    {
        var result = HeldOutResult.Evaluate(
            [new() { Label = "area-B", Score = [1f, 0f] },
             new() { Label = "area-B", Score = [0.7f, 0.3f] }],
            ["area-A", "area-B"]);
        Assert.AreEqual(2, result.Total);
        Assert.HasCount(56, result.Targets);
        foreach (var target in result.Targets)
        {
            Assert.IsNull(target.Threshold);
            Assert.IsNull(target.Precision);
            Assert.AreEqual(0, target.Accepted);
        }
    }

    [TestMethod]
    public void EvaluationUsesModelLabelsAndCountsUnseenLabels()
    {
        var context = new MLContext(seed: 42);
        string[] labels = ["area-C", "area-B"];
        var data = context.Data.LoadFromEnumerable(Enumerable.Range(0, 100).Select(index => new ModelInput
        {
            Label = labels[index % 2],
            Title = labels[index % 2]
        }));
        var model = context.Transforms.Conversion.MapValueToKey("LabelKey", nameof(ModelInput.Label))
            .Append(context.Transforms.Text.FeaturizeText("Features", nameof(ModelInput.Title)))
            .Append(context.MulticlassClassification.Trainers.SdcaMaximumEntropy("LabelKey"))
            .Fit(data);
        Assert.AreEqual(100, HeldOutResult.Evaluate(context, model, data).Targets[0].Correct);

        var testSet = context.Data.LoadFromEnumerable(new ModelInput[]
        {
            new() { Label = "area-C", Title = "area-C" },
            new() { Label = "area-B", Title = "area-B" },
            new() { Label = "area-A", Title = "area-C" }
        });

        var result = HeldOutResult.Evaluate(context, model, testSet);

        Assert.AreEqual(3, result.Total);
        Assert.AreEqual(3, result.Targets[0].Accepted);
        Assert.AreEqual(2, result.Targets[0].Correct);
    }

    [TestMethod]
    public void EvaluationHidesTheLabelColumnFromTheModel()
    {
        var context = new MLContext(seed: 42);
        string[] labels = ["area-A", "area-B"];
        var data = context.Data.LoadFromEnumerable(Enumerable.Range(0, 100).Select(index => new ModelInput { Label = labels[index % 2] }));
        var leakyModel = context.Transforms.Conversion.MapValueToKey("LabelKey", nameof(ModelInput.Label))
            .Append(context.Transforms.Text.FeaturizeText("Features", nameof(ModelInput.Label)))
            .Append(context.MulticlassClassification.Trainers.SdcaMaximumEntropy("LabelKey"))
            .Fit(data);
        Assert.AreEqual(1.0, context.MulticlassClassification.Evaluate(leakyModel.Transform(data), "LabelKey").MicroAccuracy);
        string modelPath = Path.Combine(directory, "leaky.zip");
        context.Model.Save(leakyModel, data.Schema, modelPath);

        var result = HeldOutResult.Evaluate(context, context.Model.Load(modelPath, out _), data);

        Assert.AreEqual(100, result.Targets[0].Accepted);
        Assert.AreEqual(50, result.Targets[0].Correct);
    }

    [TestMethod]
    public void HeldOutReportChartsNewAndActiveAcrossPrecisionTargets()
    {
        PrecisionTargetResult[] targets = [.. Enumerable.Range(40, 56)
            .Select(target => new PrecisionTargetResult(target, 0.8f, 100 - target, 100 - target))];
        var result = new HeldOutResult(100, targets);
        var active = new HeldOutResult(100, targets.Select(target => target with { Accepted = target.Accepted / 2 }).ToArray());
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            string report = result.ToMarkdown(active);

            StringAssert.Contains(report, "x-axis \"Minimum precision (%)\" 40 --> 95");
            StringAssert.Contains(report, "line \"New\" [60 \"40%\", 59, 58");
            StringAssert.Contains(report, "10 \"90%\", 9, 8, 7, 6, 5 \"95%\"]");
            StringAssert.Contains(report, "line \"ACTIVE\" [30, 29, 29, 28, 28, 27, 27");
            StringAssert.Contains(report, "Each point shows the maximum test-set coverage");
            StringAssert.Contains(report, "Blue: New. Orange: ACTIVE.");
            StringAssert.Contains(report, "ACTIVE may have trained on these items");
            StringAssert.Contains(report, "Thresholds are selected and evaluated on this same split");
            Assert.IsFalse(report.Split('\n').Any(line => line.StartsWith("| ")));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [DataRow(ModelType.Issue)]
    [DataRow(ModelType.PullRequest)]
    [TestMethod]
    public async Task TrainingProducesUsableModel(ModelType type)
    {
        string dataPath = WriteTrainingData(type);
        string modelPath = Path.Combine(directory, "model.zip");
        string dataArgument = type == ModelType.Issue ? "--issues-data" : "--pulls-data";
        string modelArgument = type == ModelType.Issue ? "--issues-model" : "--pulls-model";
        var result = await RunTrainer(dataArgument, dataPath, modelArgument, modelPath);
        Assert.AreEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Summary, "x-axis \"Minimum precision (%)\" 40 --> 95");
        StringAssert.Contains(result.Summary, "line \"New\"");
        Assert.IsFalse(result.Summary.Contains("line \"ACTIVE\"", StringComparison.Ordinal));

        string activeModelPath = Path.Combine(directory, "active.zip");
        File.Move(modelPath, activeModelPath);
        byte[] activeModelBytes = File.ReadAllBytes(activeModelPath);
        result = await RunTrainer(dataArgument, dataPath, modelArgument, modelPath,
            type == ModelType.Issue ? "--issues-active-model" : "--pulls-active-model", activeModelPath);

        Assert.AreEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Summary, "line \"New\"");
        StringAssert.Contains(result.Summary, "line \"ACTIVE\"");
        CollectionAssert.AreEqual(activeModelBytes, File.ReadAllBytes(activeModelPath));

        StringAssert.Contains(result.Summary, "40%\"");
        StringAssert.Contains(result.Summary, "95%\"");
        var context = new MLContext();
        using var predictor = context.Model.CreatePredictionEngine<ModelInput, ModelOutput>(context.Model.Load(modelPath, out _));
        var input = new ModelInput { Title = "Item 0", Body = "Content", FileNames = "File0.cs", FolderNames = "Folder0" };
        float[] scores = predictor.Predict(input).Score;
        Assert.HasCount(3, scores);
        Assert.IsTrue(scores.All(float.IsFinite));
    }

    [DataRow("missing")]
    [DataRow("corrupt")]
    [DataRow("incompatible")]
    [TestMethod]
    public async Task UnusableActiveModelSkipsComparison(string problem)
    {
        string dataPath = WriteTrainingData(ModelType.Issue);
        string modelPath = Path.Combine(directory, "model.zip");
        string activeModelPath = Path.Combine(directory, "active.zip");
        if (problem == "corrupt")
        {
            File.WriteAllText(activeModelPath, "Not a model archive");
        }
        else if (problem == "incompatible")
        {
            // A pull request model needs columns that the issues data doesn't have.
            var pullsResult = await RunTrainer(
                "--pulls-data", WriteTrainingData(ModelType.PullRequest, "pulls.tsv"), "--pulls-model", activeModelPath);
            Assert.AreEqual(0, pullsResult.ExitCode, pullsResult.Output);
        }

        var result = await RunTrainer(
            "--issues-data", dataPath, "--issues-model", modelPath, "--issues-active-model", activeModelPath);

        Assert.AreEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, $"ACTIVE model '{activeModelPath}' could not be evaluated");
        StringAssert.Contains(result.Summary, "> Warning: ACTIVE comparison skipped.");
        Assert.IsFalse(result.Summary.Contains("line \"ACTIVE\"", StringComparison.Ordinal));
        Assert.IsTrue(File.Exists(modelPath));
    }

    [DataRow("--issues-data", "--issues-model", "--pulls-active-model")]
    [DataRow("--pulls-data", "--pulls-model", "--issues-active-model")]
    [TestMethod]
    public async Task ActiveModelRequiresCorrespondingTrainingPaths(string dataArgument, string modelArgument, string activeArgument)
    {
        var result = await RunTrainer(dataArgument, "data.tsv", modelArgument, "model.zip", activeArgument, "active.zip");

        Assert.AreEqual(1, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "Invalid or missing arguments");
    }

    private string WriteTrainingData(ModelType type, string fileName = "data.tsv")
    {
        string dataPath = Path.Combine(directory, fileName);
        string[] labels = ["area-A", "area-B", "area-C"];
        string[] records = [type == ModelType.Issue ? "Label\tTitle\tBody" : "Label\tTitle\tBody\tFileNames\tFolderNames",
            .. Enumerable.Range(0, 300).Select(index => type == ModelType.Issue
                ? DataFileUtils.FormatIssueRecord(labels[index % 3], $"Item {index}", $"Content for {labels[index % 3]}")
                : DataFileUtils.FormatPullRequestRecord(labels[index % 3], $"Item {index}", $"Content for {labels[index % 3]}",
                    [$"File{index % 3}.cs"], [$"Folder{index % 3}"]))];
        File.WriteAllLines(dataPath, records);

        return dataPath;
    }

    private async Task<(int ExitCode, string Output, string Summary)> RunTrainer(params string[] arguments)
    {
        string summaryPath = Path.Combine(directory, "summary.md");
        File.WriteAllText(summaryPath, string.Empty);
        using var process = new Process
        {
            StartInfo = new("dotnet")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.StartInfo.ArgumentList.Add(typeof(Args).Assembly.Location);
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        process.StartInfo.Environment.Remove("GITHUB_TOKEN");
        process.StartInfo.Environment["GITHUB_STEP_SUMMARY"] = summaryPath;
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            return (process.ExitCode, await output + await error, File.ReadAllText(summaryPath));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    public sealed class ModelInput
    {
        public string? Label { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string FileNames { get; set; } = string.Empty;
        public string FolderNames { get; set; } = string.Empty;
    }

    public sealed class ModelOutput
    {
        public float[] Score { get; set; } = [];
    }
}
