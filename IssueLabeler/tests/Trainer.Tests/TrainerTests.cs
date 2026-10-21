// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

extern alias Tester;

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
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
    public void PrecisionTargetsChooseMaximumCoverageEvenWhenPrecisionRecovers()
    {
        int[] wrongRanks = [2, 11, 12, 16, 17, 18, 21, 22, 23, 24];
        HeldOutPrediction[] predictions = [.. Enumerable.Range(1, 25).Reverse().Select(rank => new HeldOutPrediction
        {
            Label = wrongRanks.Contains(rank) ? "area-B" : "area-A",
            Score = [(100 - rank) / 100f, rank / 100f]
        })];
        var result = Trainer.Evaluate(predictions, ["area-A", "area-B"]);

        Assert.AreEqual(25, result.Total);
        PrecisionTargetResult[] expected =
        [
            new(60, 0.75f, 15, 25),
            new(70, 0.8f, 14, 20),
            new(80, 0.85f, 12, 15),
            new(90, 0.9f, 9, 10),
            new(95, 0.99f, 1, 1)
        ];
        CollectionAssert.AreEqual(expected, result.Targets);
        StringAssert.Contains(result.ToMarkdown(), "| 90% | 0.9 | 90.00 % | 40.00 % | 10 |");
    }

    [TestMethod]
    public void TiedPredictionScoresAreAcceptedTogetherAtTheThreshold()
    {
        var result = Trainer.Evaluate(
            [new() { Label = "area-A", Score = [0.9f, 0.1f] },
             new() { Label = "area-B", Score = [0.9f, 0.1f] },
             new() { Label = "area-A", Score = [0.8f, 0.2f] }],
            ["area-A", "area-B"]);
        Assert.AreEqual(new PrecisionTargetResult(60, 0.8f, 2, 3), result.Targets[0]);
        Assert.IsTrue(result.Targets.Skip(1).All(target => target.Threshold is null));
    }

    [TestMethod]
    public void EqualClassScoresUseFirstSlotAndCaseInsensitiveLabels()
    {
        var result = Trainer.Evaluate(
            [new() { Label = "AREA-A", Score = [0.5f, 0.5f] }],
            ["area-A", "area-B"]);
        foreach (var target in result.Targets)
        {
            Assert.AreEqual(0.5f, target.Threshold);
            Assert.AreEqual(1, target.Correct);
            Assert.AreEqual(1, target.Accepted);
        }
    }

    [TestMethod]
    public void CaseVariantSlotsAndUnseenGoldLabelsAreCountedCorrectly()
    {
        var result = Trainer.Evaluate(
            [new() { Label = "area-Foo", Score = [0.1f, 0.8f, 0.1f] },
             new() { Label = "area-foo", Score = [0.8f, 0.1f, 0.1f] },
             new() { Label = "area-Unseen", Score = [0.1f, 0.1f, 0.8f] }],
            ["area-Foo", "area-foo", "area-Bar"]);
        Assert.AreEqual(new PrecisionTargetResult(60, 0.8f, 2, 3), result.Targets[0]);
        Assert.IsTrue(result.Targets.Skip(1).All(target => target.Threshold is null));
    }

    [TestMethod]
    public void UnreachableTargetsAreNotReportedAsPerfectPrecisionWithNoCoverage()
    {
        var result = Trainer.Evaluate(
            [new() { Label = "area-B", Score = [1f, 0f] },
             new() { Label = "area-B", Score = [0.7f, 0.3f] }],
            ["area-A", "area-B"]);
        Assert.AreEqual(2, result.Total);
        Assert.HasCount(5, result.Targets);
        foreach (var target in result.Targets)
        {
            Assert.IsNull(target.Threshold);
            Assert.IsNull(target.Precision);
            Assert.AreEqual(0, target.Accepted);
            StringAssert.Contains(result.ToMarkdown(), $"| {target.TargetPrecision}% | Not reached | N/A | N/A | N/A |");
        }
    }

    [TestMethod]
    public void PrecisionTargetsMatchAnExhaustiveThresholdSweep()
    {
        var random = new Random(42);
        for (int sample = 0; sample < 50; sample++)
        {
            HeldOutPrediction[] predictions = [.. Enumerable.Range(0, random.Next(1, 40)).Select(_ =>
            {
                float score = random.Next(50, 101) / 100f;
                return new HeldOutPrediction
                {
                    Label = random.Next(2) == 0 ? "area-A" : "area-B",
                    Score = [score, 1 - score]
                };
            })];
            var result = Trainer.Evaluate(predictions, ["area-A", "area-B"]);
            foreach (var target in result.Targets)
            {
                var expected = predictions.Select(prediction => prediction.Score[0]).Distinct()
                    .Select(threshold =>
                    {
                        var accepted = predictions.Where(prediction => prediction.Score[0] >= threshold).ToArray();
                        int correct = accepted.Count(prediction => prediction.Label == "area-A");
                        return new PrecisionTargetResult(target.TargetPrecision, threshold, correct, accepted.Length);
                    })
                    .Where(candidate => (decimal)candidate.Correct / candidate.Accepted >= target.TargetPrecision / 100m)
                    .OrderByDescending(candidate => candidate.Accepted)
                    .FirstOrDefault();

                Assert.AreEqual(expected ?? new(target.TargetPrecision, null, 0, 0), target,
                    $"Sample {sample}, target {target.TargetPrecision}%.");
            }
        }
    }

    [TestMethod]
    public void EmptyHoldoutAndMalformedScoresFailExplicitly()
    {
        Assert.Throws<InvalidOperationException>(() => Trainer.Evaluate([], ["area-A"]));
        Assert.Throws<InvalidDataException>(() => Trainer.Evaluate(
            [new() { Label = "area-A", Score = [] }], ["area-A"]));
        Assert.Throws<InvalidDataException>(() => Trainer.Evaluate(
            [new() { Label = "area-A", Score = [float.NaN] }], ["area-A"]));
        Assert.Throws<InvalidDataException>(() => Trainer.Evaluate(
            [new() { Label = "area-A", Score = [float.PositiveInfinity] }], ["area-A"]));
        Assert.Throws<InvalidDataException>(() => Trainer.Evaluate(
            [new() { Label = string.Empty, Score = [1f] }], ["area-A"]));
    }

    [TestMethod]
    public async Task TrainerNoLongerAcceptsASingleThreshold()
    {
        var result = await Run(typeof(Args).Assembly.Location, false,
            "--issues-data", "missing.tsv", "--issues-model", "model.zip", "--threshold", "0.4");
        Assert.AreEqual(1, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "Unrecognized argument: --threshold");
        Assert.IsFalse(File.Exists(Path.Combine(directory, "model.zip")));
    }

    [TestMethod]
    public void ReportUsesInvariantRoundTripThresholds()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            float score = float.BitIncrement(0.8f);
            var result = Trainer.Evaluate(
                [new() { Label = "area-A", Score = [score, 1 - score] }],
                ["area-A", "area-B"]);
            string threshold = score.ToString("R", CultureInfo.InvariantCulture);
            string report = result.ToMarkdown();
            StringAssert.Contains(report, $"| 60% | {threshold} | 100.00 % | 100.00 % | 1 |");
            StringAssert.Contains(report, "Accepted is the number of predictions kept at this cutoff, including incorrect predictions.");
            StringAssert.Contains(report, "| Coverage | Accepted |");
            StringAssert.Contains(report, "Each row aims to label as many test items as possible, with at least the target percentage labeled correctly.");
            StringAssert.Contains(report, "selected and evaluated on this same split");
            Assert.IsFalse(report.Contains("These exploratory results", StringComparison.Ordinal));
            Assert.IsFalse(report.Contains("Scores are not guaranteed", StringComparison.Ordinal));
            Assert.IsFalse(report.Contains("Production prediction settings", StringComparison.Ordinal));
            Assert.IsFalse(report.Contains("Not reached means", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [DataRow(ModelType.Issue)]
    [DataRow(ModelType.PullRequest)]
    [TestMethod]
    public async Task TrainingReportsPrecisionTargetsWithoutCachingTheSplit(ModelType type)
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
            type == ModelType.Issue ? "--issues-model" : "--pulls-model", modelPath);
        Assert.AreEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Summary, "| Metric | Value | Meaning |");
        StringAssert.Contains(result.Summary, "Average accuracy across labels, each weighted equally");
        StringAssert.Contains(result.Summary, "Fraction of all items predicted correctly");
        StringAssert.Contains(result.Summary, "Penalizes low scores for the correct label");
        StringAssert.Contains(result.Summary, "**Per-class LogLoss** (first three label categories, lower is better)");
        StringAssert.Contains(result.Summary, "| Class | LogLoss |");
        for (int index = 1; index <= 3; index++)
        {
            Assert.IsTrue(Regex.IsMatch(result.Summary,
                $@"^\| Class {index} \| \S+ \|\r?$", RegexOptions.Multiline), result.Summary);
        }
        StringAssert.Contains(result.Output, "| Metric | Value | Meaning |");
        StringAssert.Contains(result.Summary, "### Held-out evaluation");
        StringAssert.Contains(result.Output, "| Target precision | Threshold | Observed precision | Coverage | Accepted |");
        Assert.IsTrue(Regex.IsMatch(result.Summary, @"\r?\n\r?\n### Held-out evaluation"), result.Summary);
        var population = Regex.Match(result.Summary, @"Evaluated (\d+) items");
        Assert.IsTrue(population.Success, result.Summary);
        int total = int.Parse(population.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.IsTrue(total > 0 && total < 300, result.Summary);
        var rows = Regex.Matches(result.Summary,
            @"^\| (60|70|80|90|95)% \| ([^|]+) \| ([^|]+) \| ([^|]+) \| ([^|]+) \|\r?$", RegexOptions.Multiline);
        Assert.HasCount(5, rows, result.Summary);
        foreach (Match row in rows)
        {
            if (row.Groups[2].Value == "Not reached")
            {
                Assert.AreEqual("N/A", row.Groups[3].Value);
                Assert.AreEqual("N/A", row.Groups[4].Value);
                Assert.AreEqual("N/A", row.Groups[5].Value);
                continue;
            }

            float threshold = float.Parse(row.Groups[2].Value, CultureInfo.InvariantCulture);
            double precision = double.Parse(row.Groups[3].Value.TrimEnd(' ', '%'), CultureInfo.InvariantCulture);
            double coverage = double.Parse(row.Groups[4].Value.TrimEnd(' ', '%'), CultureInfo.InvariantCulture);
            int accepted = int.Parse(row.Groups[5].Value, CultureInfo.InvariantCulture);
            Assert.IsTrue(threshold is >= 0 and <= 1, row.Value);
            Assert.IsTrue(precision >= int.Parse(row.Groups[1].Value, CultureInfo.InvariantCulture), row.Value);
            Assert.IsTrue(accepted > 0 && accepted <= total, row.Value);
            Assert.AreEqual(100.0 * accepted / total, coverage, 0.005001, row.Value);
            if (type == ModelType.PullRequest && accepted == total)
            {
                string accuracy = Regex.Match(result.Summary, @"\| MicroAccuracy \| (\S+) \|").Groups[1].Value;
                Assert.AreEqual(double.Parse(accuracy, CultureInfo.InvariantCulture), precision / 100, 0.00011);
            }
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
        Assert.IsFalse(result.Output.Contains("Use the training job's held-out report", StringComparison.Ordinal));
        Assert.IsFalse(result.Summary.Contains("Use the training job's held-out report", StringComparison.Ordinal));
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
