// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

extern alias Tester;

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Actions.Core.Extensions;
using Actions.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

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
    public void ThresholdChangesAcceptanceOnTheSameHeldOutPredictions()
    {
        HeldOutPrediction[] predictions =
        [
            new() { Label = "area-A", Score = [0.8f, 0.2f] },
            new() { Label = "area-B", Score = [0.7f, 0.3f] },
            new() { Label = "area-B", Score = [0.4f, 0.6f] }
        ];
        var all = Trainer.Evaluate(predictions, ["area-A", "area-B"], 0);
        Assert.AreEqual(2, all.Correct);
        Assert.AreEqual(1, all.Wrong);
        Assert.AreEqual(0, all.Abstained);
        var filtered = Trainer.Evaluate(predictions, ["area-A", "area-B"], 0.7f);
        Assert.AreEqual(1, filtered.Correct);
        Assert.AreEqual(1, filtered.Wrong);
        Assert.AreEqual(1, filtered.Abstained);
        Assert.AreEqual(0.5, filtered.Precision);
        Assert.AreEqual(2.0 / 3, filtered.Coverage, 0.000001);
        Assert.AreEqual(3, filtered.Total);
        var none = Trainer.Evaluate(predictions, ["area-A", "area-B"], 1);
        Assert.AreEqual(0, none.Accepted);
        Assert.AreEqual(3, none.Abstained);
        Assert.IsNull(none.Precision);
        Assert.AreEqual(0, none.Coverage);
        StringAssert.Contains(none.ToMarkdown(1), "| 3 | 0 | 0 | 3 | N/A |");
    }

    [TestMethod]
    public void EqualScoresUseSlotOrderAndAcceptTheThresholdBoundary()
    {
        var result = Trainer.Evaluate(
            [new() { Label = "AREA-A", Score = [0.5f, 0.5f] },
             new() { Label = "area-B", Score = [0.5f, 0.5f] }],
            ["area-A", "area-B"], 0.5f);
        Assert.AreEqual(1, result.Correct);
        Assert.AreEqual(1, result.Wrong);
        Assert.AreEqual(0, result.Abstained);
    }

    [TestMethod]
    public void CaseVariantSlotsAndUnseenGoldLabelsAreCountedCorrectly()
    {
        var result = Trainer.Evaluate(
            [new() { Label = "area-Foo", Score = [0.1f, 0.8f, 0.1f] },
             new() { Label = "area-foo", Score = [0.8f, 0.1f, 0.1f] },
             new() { Label = "area-Unseen", Score = [0.1f, 0.1f, 0.8f] }],
            ["area-Foo", "area-foo", "area-Bar"], 0.4f);
        Assert.AreEqual(2, result.Correct);
        Assert.AreEqual(1, result.Wrong);
    }

    [TestMethod]
    public void EmptyHoldoutAndMalformedScoresFailExplicitly()
    {
        Assert.Throws<InvalidOperationException>(() => Trainer.Evaluate([], ["area-A"], 0.4f));
        Assert.Throws<InvalidDataException>(() => Trainer.Evaluate(
            [new() { Label = "area-A", Score = [] }], ["area-A"], 0.4f));
        Assert.Throws<InvalidDataException>(() => Trainer.Evaluate(
            [new() { Label = "area-A", Score = [float.NaN] }], ["area-A"], 0.4f));
    }

    [DataRow("NaN")]
    [DataRow("Infinity")]
    [DataRow("-0.01")]
    [DataRow("1.01")]
    [DataRow("0,15")]
    [DataRow("0.1,0.9")]
    [TestMethod]
    public async Task InvalidThresholdFailsBeforeTraining(string threshold)
    {
        var result = await Run(typeof(Args).Assembly.Location, false,
            "--issues-data", "missing.tsv", "--issues-model", "model.zip", "--threshold", threshold);
        Assert.AreEqual(1, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "finite invariant decimal between 0 and 1");
        Assert.IsFalse(File.Exists(Path.Combine(directory, "model.zip")));
    }

    [TestMethod]
    public void ThresholdDefaultsAndInvariantParsingArePreserved()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            using var provider = new ServiceCollection().AddGitHubActionsCore().BuildServiceProvider();
            var action = provider.GetRequiredService<ICoreService>();
            string[] arguments = ["--issues-data", "data.tsv", "--issues-model", "model.zip"];
            var defaults = Args.Parse(arguments, action) ?? throw new AssertFailedException("Arguments rejected.");
            Assert.AreEqual(0.4f, defaults.Threshold);
            var explicitValue = Args.Parse([.. arguments, "--threshold", "0.15"], action)
                ?? throw new AssertFailedException("Invariant threshold rejected.");
            Assert.AreEqual(0.15f, explicitValue.Threshold);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [DataRow(ModelType.Issue, "0.15")]
    [DataRow(ModelType.PullRequest, "0")]
    [TestMethod]
    public async Task TrainingReportsHeldOutMetricsWithoutCachingTheSplit(ModelType type, string threshold)
    {
        string dataPath = Path.Combine(directory, "data.tsv");
        string modelPath = Path.Combine(directory, "model.zip");
        string[] labels = type == ModelType.Issue ? ["area-Foo", "area-foo", "area-Bar"] : ["area-A", "area-B", "area-C"];
        string[] records = [type == ModelType.Issue ? "Label\tTitle\tBody" : "Label\tTitle\tBody\tFileNames\tFolderNames",
            .. Enumerable.Range(0, 300).Select(index => type == ModelType.Issue
                ? DataFileUtils.FormatIssueRecord(labels[index % 3], $"Item {index}", $"Content for {labels[index % 3]}")
                : DataFileUtils.FormatPullRequestRecord(labels[index % 3], $"Item {index}", $"Content for {labels[index % 3]}",
                    [$"File{index % 3}.cs"], [$"Folder{index % 3}"]))];
        File.WriteAllLines(dataPath, records);
        byte[] source = File.ReadAllBytes(dataPath);
        var result = await Run(typeof(Args).Assembly.Location, false,
            type == ModelType.Issue ? "--issues-data" : "--pulls-data", dataPath,
            type == ModelType.Issue ? "--issues-model" : "--pulls-model", modelPath,
            "--threshold", threshold);
        Assert.AreEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Summary, "MacroAccuracy");
        StringAssert.Contains(result.Summary, "MicroAccuracy");
        StringAssert.Contains(result.Summary, "LogLoss");
        StringAssert.Contains(result.Summary, "### Held-out evaluation");
        StringAssert.Contains(result.Summary, $"equal to **{threshold}**");
        Assert.IsTrue(Regex.IsMatch(result.Summary, @"\r?\n\r?\n### Held-out evaluation"), result.Summary);
        var counts = Regex.Match(result.Summary, @"\| (\d+) \| (\d+) \| (\d+) \| (\d+) \|");
        Assert.IsTrue(counts.Success, result.Summary);
        int total = int.Parse(counts.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.IsTrue(total > 0 && total < 300, result.Summary);
        Assert.AreEqual(total, int.Parse(counts.Groups[2].Value, CultureInfo.InvariantCulture)
            + int.Parse(counts.Groups[3].Value, CultureInfo.InvariantCulture)
            + int.Parse(counts.Groups[4].Value, CultureInfo.InvariantCulture));
        if (threshold == "0")
        {
            Assert.AreEqual("0", counts.Groups[4].Value);
            string accuracy = Regex.Match(result.Summary, @"MicroAccuracy: (\S+)").Groups[1].Value;
            Assert.AreEqual(double.Parse(accuracy, CultureInfo.CurrentCulture),
                double.Parse(counts.Groups[2].Value, CultureInfo.InvariantCulture) / total, 0.0001,
                "At threshold zero the new counts must agree with the existing held-out micro accuracy.");
        }
        CollectionAssert.AreEqual(source, File.ReadAllBytes(dataPath));
        CollectionAssert.AreEquivalent(new[] { "data.tsv", "model.zip", "summary.md" },
            Directory.GetFiles(directory).Select(Path.GetFileName).ToArray());
        using (var archive = ZipFile.OpenRead(modelPath))
        {
            Assert.IsNull(archive.GetEntry("DatasetSplit.sha256"));
        }
        File.Delete(dataPath);
        var context = new MLContext();
        using var predictor = context.Model.CreatePredictionEngine<ModelInput, ModelOutput>(context.Model.Load(modelPath, out _));
        var input = new ModelInput { Title = "Item 0", Body = "Content", FileNames = "File0.cs", FolderNames = "Folder0" };
        var scores = predictor.Predict(input).Score.ToArray();
        Assert.HasCount(3, scores);
        foreach (string label in labels)
        {
            input.Label = label;
            CollectionAssert.AreEqual(scores, predictor.Predict(input).Score);
        }
    }

    [TestMethod]
    public async Task HistoricalTestStillRequiresGitHubCredentials()
    {
        var result = await Run(typeof(Tester::Args).Assembly.Location, false,
            "--repo", "dotnet/example", "--label-prefix", "area-", "--issues-model", "model.zip");
        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.Output, "GITHUB_TOKEN is empty");
    }

    [TestMethod]
    public async Task HistoricalTestWarnsBeforeLoadingAModelOrDownloading()
    {
        var result = await Run(typeof(Tester::Args).Assembly.Location, true,
            "--repo", "dotnet/example", "--label-prefix", "area-", "--issues-model", Path.Combine(directory, "missing.zip"));
        Assert.AreNotEqual(0, result.ExitCode);
        StringAssert.Contains(result.Output, "may include training data");
        StringAssert.Contains(result.Summary, "> [!WARNING]");
        StringAssert.Contains(result.Summary, "not an independent held-out evaluation");
        StringAssert.Contains(result.Summary, "training job's held-out report");
    }

    private async Task<(int ExitCode, string Output, string Summary)> Run(string assemblyPath, bool token, params string[] arguments)
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
        process.StartInfo.ArgumentList.Add(assemblyPath);
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        process.StartInfo.Environment.Remove("GITHUB_TOKEN");
        if (token)
        {
            process.StartInfo.Environment["GITHUB_TOKEN"] = "test-token";
        }
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
