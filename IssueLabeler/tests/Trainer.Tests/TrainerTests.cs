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
        PrecisionTargetResult[] expected =
        [
            new(40, 0.75f, 15, 25),
            new(50, 0.75f, 15, 25),
            new(60, 0.75f, 15, 25),
            new(70, 0.8f, 14, 20),
            new(80, 0.85f, 12, 15),
            new(85, 0.9f, 9, 10),
            new(90, 0.9f, 9, 10),
            new(95, 0.99f, 1, 1),
            new(98, 0.99f, 1, 1),
            new(99, 0.99f, 1, 1)
        ];
        CollectionAssert.AreEqual(expected, result.Targets);
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
        Assert.AreEqual(new PrecisionTargetResult(50, 0.8f, 2, 3), result.Targets[1]);
        Assert.AreEqual(new PrecisionTargetResult(60, 0.8f, 2, 3), result.Targets[2]);
        Assert.IsTrue(result.Targets.Skip(3).All(target => target.Threshold is null));
    }

    [TestMethod]
    public void UnreachableTargetsAreNotReportedAsPerfectPrecisionWithNoCoverage()
    {
        var result = HeldOutResult.Evaluate(
            [new() { Label = "area-B", Score = [1f, 0f] },
             new() { Label = "area-B", Score = [0.7f, 0.3f] }],
            ["area-A", "area-B"]);
        Assert.AreEqual(2, result.Total);
        Assert.HasCount(10, result.Targets);
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
    public void ComparisonReportUsesOneRowPerTargetAndInvariantDeployableThresholds()
    {
        var candidate = new HeldOutResult(3, [new(40, 0.8f, 2, 3), new(99, null, 0, 0)]);
        var active = new HeldOutResult(3, [new(40, 0.8123457f, 3, 3), new(99, 0.8123457f, 3, 3)]);
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            string report = candidate.ToMarkdown(active);

            StringAssert.Contains(report, "| Minimum precision | New threshold | New precision | New coverage | ACTIVE threshold | ACTIVE precision | ACTIVE coverage |");
            StringAssert.Contains(report, "| 40% | 0.8 | 66.67 % | 100.00 % (3) | 0.8123457 | 100.00 % | 100.00 % (3) |");
            StringAssert.Contains(report, "| 99% | Not reached | N/A | N/A | 0.8123457 | 100.00 % | 100.00 % (3) |");
            StringAssert.Contains(report, "> Note: ACTIVE may have trained on these items, so its results may be optimistic.");
            StringAssert.Contains(report, "Promotion does not update the prediction threshold.");
            Assert.AreEqual(2, report.Split('\n').Count(line => line.StartsWith("| ") && line.Contains("% |")));
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
        StringAssert.Contains(result.Summary, "No ACTIVE model was provided.");
        Assert.IsFalse(result.Summary.Contains("| ACTIVE threshold |"));
        Assert.IsFalse(result.Summary.Contains("> Note: ACTIVE"));

        string activeModelPath = Path.Combine(directory, "active.zip");
        File.Move(modelPath, activeModelPath);
        byte[] activeModelBytes = File.ReadAllBytes(activeModelPath);
        result = await RunTrainer(dataArgument, dataPath, modelArgument, modelPath,
            type == ModelType.Issue ? "--issues-active-model" : "--pulls-active-model", activeModelPath);

        Assert.AreEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Summary, "| ACTIVE threshold | ACTIVE precision | ACTIVE coverage |");
        StringAssert.Contains(result.Summary, "ACTIVE may have trained on these items");
        CollectionAssert.AreEqual(activeModelBytes, File.ReadAllBytes(activeModelPath));

        StringAssert.Contains(result.Summary, "| 40% |");
        StringAssert.Contains(result.Summary, "| 99% |");
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
        Assert.IsFalse(result.Summary.Contains("| ACTIVE threshold |"));
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
