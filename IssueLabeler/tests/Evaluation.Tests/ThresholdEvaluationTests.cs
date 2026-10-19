// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Nodes;
using IssueLabeler.Evaluation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.ClassLevel)]

namespace Evaluation.Tests;

[TestClass]
public sealed class ThresholdEvaluationTests
{
    private string directory = string.Empty;
    private string modelPath = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        directory = Path.Combine(Path.GetTempPath(), $"issue-labeler-evaluation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        modelPath = Path.Combine(directory, "model.zip");
        File.WriteAllText(modelPath, "model identity");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(directory, recursive: true);

    [TestMethod]
    public void ScoresRoundTripAndAreBoundToTheModel()
    {
        var artifact = CreateArtifact();
        string path = EvaluationArtifact.GetPath(modelPath);
        artifact.Save(path);
        var loaded = EvaluationArtifact.Load(path, modelPath);
        CollectionAssert.AreEqual(artifact.Rows[0].Scores, loaded.Rows[0].Scores);
        Assert.AreEqual(artifact.DatasetSha256, loaded.DatasetSha256);
        File.WriteAllText(modelPath, "different model");
        Assert.ThrowsExactly<InvalidDataException>(() => EvaluationArtifact.Load(path, modelPath));
    }

    [TestMethod]
    public void MissingArtifactDoesNotFallBackToOtherData()
    {
        Assert.ThrowsExactly<FileNotFoundException>(() => EvaluationArtifact.Load(Path.Combine(directory, "missing.json"), modelPath));
    }

    [TestMethod]
    public void InvalidSchemaAndMissingRequiredMetadataAreRejected()
    {
        var artifact = CreateArtifact();
        string path = EvaluationArtifact.GetPath(modelPath);
        artifact.Save(path);
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        json["schemaVersion"] = 99;
        File.WriteAllText(path, json.ToJsonString());
        Assert.ThrowsExactly<InvalidDataException>(() => EvaluationArtifact.Load(path, modelPath));
        json["schemaVersion"] = 1;
        json.AsObject().Remove("modelSha256");
        File.WriteAllText(path, json.ToJsonString());
        Assert.ThrowsExactly<JsonException>(() => EvaluationArtifact.Load(path, modelPath));
    }

    [DataRow("schemaVersion")]
    [DataRow("trainingSupport")]
    [DataRow("labels")]
    [DataRow("rows")]
    [TestMethod]
    public void MissingRequiredFieldsAreRejected(string field)
    {
        string path = EvaluationArtifact.GetPath(modelPath);
        CreateArtifact().Save(path);
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        json.AsObject().Remove(field);
        File.WriteAllText(path, json.ToJsonString());
        Assert.ThrowsExactly<JsonException>(() => EvaluationArtifact.Load(path, modelPath));
    }

    [TestMethod]
    public void MalformedJsonIsRejected()
    {
        string path = EvaluationArtifact.GetPath(modelPath);
        File.WriteAllText(path, "{");
        Assert.ThrowsExactly<JsonException>(() => EvaluationArtifact.Load(path, modelPath));
    }

    [DataRow(float.NaN, 0.2f)]
    [DataRow(float.PositiveInfinity, 0.2f)]
    [DataRow(-0.1f, 1.1f)]
    [DataRow(0.1f, 0.2f)]
    [TestMethod]
    public void InvalidScoreVectorsAreRejected(float first, float second)
    {
        var artifact = CreateArtifact([new() { Index = 0, Label = "area-A", Scores = [first, second] }]);
        Assert.ThrowsExactly<InvalidDataException>(artifact.Validate);
    }

    [TestMethod]
    public void MissingRowsWrongSlotCountsAndDuplicateRowIndicesAreRejected()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => CreateArtifact([]).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => CreateArtifact(
            [new() { Index = 0, Label = "area-A", Scores = [1] }]).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => CreateArtifact(
            [new() { Index = 0, Label = "area-A", Scores = [1, 0] },
             new() { Index = 0, Label = "area-B", Scores = [0, 1] }]).Validate());
    }

    [TestMethod]
    public void ThresholdControlsAcceptanceNotTheStoredPrediction()
    {
        var artifact = CreateArtifact(
        [
            new() { Index = 0, Label = "area-A", Scores = [0.8f, 0.2f] },
            new() { Index = 1, Label = "area-B", Scores = [0.7f, 0.3f] },
            new() { Index = 2, Label = "area-B", Scores = [0.4f, 0.6f] }
        ]);
        var results = ThresholdEvaluation.Evaluate(artifact, [0, 0.7f, 1], IsArea);
        Assert.AreEqual(2, results[0].Correct);
        Assert.AreEqual(1, results[0].Wrong);
        Assert.AreEqual(0, results[0].Abstained);
        Assert.AreEqual(1, results[1].Correct);
        Assert.AreEqual(1, results[1].Wrong);
        Assert.AreEqual(1, results[1].Abstained);
        Assert.AreEqual(0.5, results[1].Precision!.Value, 0.000001);
        Assert.AreEqual(2.0 / 3, results[1].Coverage, 0.000001);
        Assert.AreEqual(3, results[2].Abstained);
        Assert.IsNull(results[2].Precision);
        Assert.IsNull(results[2].PrecisionLower);
        Assert.AreEqual(0.7f, artifact.Rows[1].Scores[0]);
        var area = results[1].Areas.Single(value => value.Label == "area-B");
        Assert.AreEqual(2, area.HeldOutSupport);
        Assert.AreEqual(2, area.Missed);
        Assert.AreEqual(1, area.Abstained);
        Assert.AreEqual(1, area.Misrouted);
        Assert.AreEqual(0.5, area.Coverage!.Value, 0.000001);
    }

    [TestMethod]
    public void EqualScoresKeepSlotOrderAndIncludeTheThresholdBoundary()
    {
        var artifact = CreateArtifact(
        [
            new() { Index = 0, Label = "area-A", Scores = [0.5f, 0.5f] },
            new() { Index = 1, Label = "area-B", Scores = [0.5f, 0.5f] }
        ]);
        var result = ThresholdEvaluation.Evaluate(artifact, [0.5f], IsArea).Single();
        Assert.AreEqual(1, result.Correct);
        Assert.AreEqual(1, result.Wrong);
        Assert.AreEqual(0, result.Abstained);
        Assert.AreEqual(1, result.Areas.Single(area => area.Label == "area-A").Wrong);
    }

    [TestMethod]
    public void CaseVariantSlotsKeepTheirIdentityAndShareOneReportArea()
    {
        var artifact = new EvaluationArtifact
        {
            ModelSha256 = EvaluationArtifact.HashFile(modelPath),
            DatasetSha256 = EvaluationArtifact.HashFile(modelPath),
            ToolVersion = "test",
            ModelType = "Issue",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            TrainingCount = 30,
            TrainingSupport = new() { ["area-Foo"] = 10, ["area-foo"] = 15, ["area-Bar"] = 5 },
            Labels = ["area-Foo", "area-foo", "area-Bar"],
            Rows =
            [
                new() { Index = 0, Label = "area-foo", Scores = [0.8f, 0.1f, 0.1f] },
                new() { Index = 1, Label = "area-Foo", Scores = [0.1f, 0.8f, 0.1f] },
                new() { Index = 2, Label = "area-Bar", Scores = [0.2f, 0.7f, 0.1f] }
            ]
        };
        string path = EvaluationArtifact.GetPath(modelPath);
        artifact.Save(path);
        var loaded = EvaluationArtifact.Load(path, modelPath);
        CollectionAssert.AreEqual(artifact.Labels, loaded.Labels);
        var result = ThresholdEvaluation.Evaluate(loaded, [0.4f], IsArea).Single();
        Assert.AreEqual(2, result.Correct);
        Assert.AreEqual(1, result.Wrong);
        Assert.HasCount(2, result.Areas);
        var area = result.Areas.Single(value => value.Label.Equals("area-Foo", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(25, area.TrainingSupport);
        Assert.AreEqual(2, area.HeldOutSupport);
        Assert.AreEqual(2, area.Correct);
        Assert.AreEqual(1, area.Wrong);
        Assert.AreEqual(2.0 / 3, area.Precision!.Value, 0.000001);
        Assert.AreEqual(artifact.TrainingCount, result.Areas.Sum(value => value.TrainingSupport));
    }

    [TestMethod]
    public void UnseenAndZeroHeldOutSupportAreVisible()
    {
        var artifact = CreateArtifact([new() { Index = 0, Label = "area-C", Scores = [1, 0] }]);
        var result = ThresholdEvaluation.Evaluate(artifact, [0.4f], IsArea).Single();
        var unseen = result.Areas.Single(value => value.Label == "area-C");
        Assert.AreEqual(0, unseen.TrainingSupport);
        Assert.AreEqual(1, unseen.HeldOutSupport);
        Assert.AreEqual(1, unseen.Missed);
        Assert.AreEqual(0, result.Areas.Single(value => value.Label == "area-B").HeldOutSupport);
    }

    [TestMethod]
    public void MismatchedPrefixAndNonfiniteThresholdsAreRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ThresholdEvaluation.Evaluate(CreateArtifact(), [0.4f], _ => false));
        Assert.ThrowsExactly<ArgumentException>(() => ThresholdEvaluation.Evaluate(CreateArtifact(), [0.4f], label => label == "area-A"));
        Assert.ThrowsExactly<ArgumentException>(() => ThresholdEvaluation.Evaluate(CreateArtifact(), [float.NaN], IsArea));
        Assert.ThrowsExactly<ArgumentException>(() => ThresholdEvaluation.Evaluate(CreateArtifact(), [], IsArea));
        Assert.ThrowsExactly<ArgumentException>(() => ThresholdEvaluation.Evaluate(CreateArtifact(), [-0.1f], IsArea));
        Assert.ThrowsExactly<ArgumentException>(() => ThresholdEvaluation.Evaluate(CreateArtifact(), [1.1f], IsArea));
    }

    [DataRow(5145, 6437, 0.7893, 0.8089)]
    [DataRow(6885, 10500, 0.6466, 0.6647)]
    [TestMethod]
    public void WilsonIntervalsMatchBenchmarkCounts(int correct, int accepted, double lower, double upper)
    {
        var interval = ThresholdResult.Wilson(correct, accepted)!.Value;
        Assert.AreEqual(lower, interval.Lower, 0.0001);
        Assert.AreEqual(upper, interval.Upper, 0.0001);
        Assert.IsNull(ThresholdResult.Wilson(0, 0));
    }

    private EvaluationArtifact CreateArtifact(EvaluationRow[]? rows = null) => new()
    {
        ModelSha256 = EvaluationArtifact.HashFile(modelPath),
        DatasetSha256 = EvaluationArtifact.HashFile(modelPath),
        ToolVersion = "test",
        ModelType = "Issue",
        CreatedAtUtc = DateTimeOffset.UtcNow,
        TrainingCount = 20,
        TrainingSupport = new() { ["area-A"] = 10, ["area-B"] = 10 },
        Labels = ["area-A", "area-B"],
        Rows = rows ?? [new() { Index = 0, Label = "area-A", Scores = [0.8f, 0.2f] }]
    };

    private static bool IsArea(string label) => label.StartsWith("area-", StringComparison.OrdinalIgnoreCase);
}
