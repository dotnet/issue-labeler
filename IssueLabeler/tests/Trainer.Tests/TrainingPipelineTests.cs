// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ML;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Trainer.Tests;

[TestClass]
public sealed class TrainingPipelineTests
{
    [TestMethod]
    [DataRow(ModelType.Issue)]
    [DataRow(ModelType.PullRequest)]
    public void ChangingLabel_ChangesOnlyTarget_NotFeaturesOrPredictions(ModelType type)
    {
        var mlContext = new MLContext(seed: 1);
        var trainingData = mlContext.Data.LoadFromEnumerable(CreateTrainingExamples());
        var model = TrainingPipeline.Create(mlContext, type).Fit(trainingData);
        TrainingExample example = CreateTrainingExamples().First();
        TrainingExample[] examples =
        [
            example,
            example with { Label = "area-network" },
            example with { Label = string.Empty }
        ];

        using var stream = new MemoryStream();
        mlContext.Model.Save(model, trainingData.Schema, stream);
        stream.Position = 0;
        var loadedModel = mlContext.Model.Load(stream, out _);
        var transformed = loadedModel.Transform(mlContext.Data.LoadFromEnumerable(examples));
        PipelineOutput[] results = mlContext.Data.CreateEnumerable<PipelineOutput>(transformed, reuseRowObject: false).ToArray();

        Assert.AreNotEqual(0u, results[0].LabelKey);
        Assert.AreNotEqual(0u, results[1].LabelKey);
        Assert.AreNotEqual(results[0].LabelKey, results[1].LabelKey);
        Assert.AreEqual(0u, results[2].LabelKey);

        foreach (PipelineOutput result in results.Skip(1))
        {
            CollectionAssert.AreEqual(results[0].Features, result.Features);
            CollectionAssert.AreEqual(results[0].Score, result.Score);
            Assert.AreEqual(results[0].PredictedLabel, result.PredictedLabel);
        }

        using var predictor = mlContext.Model.CreatePredictionEngine<TrainingExample, PipelineOutput>(loadedModel);
        PipelineOutput prediction = predictor.Predict(examples[2]);

        Assert.HasCount(2, prediction.Score);
        Assert.IsTrue(prediction.PredictedLabel is "area-compiler" or "area-network");
        CollectionAssert.AreEqual(results[0].Score, prediction.Score);
    }

    [TestMethod]
    [DataRow(ModelType.Issue)]
    [DataRow(ModelType.PullRequest)]
    public void ContentColumns_ContributeToFeatures(ModelType type)
    {
        var mlContext = new MLContext(seed: 1);
        var trainingData = mlContext.Data.LoadFromEnumerable(CreateTrainingExamples());
        var model = TrainingPipeline.Create(mlContext, type).Fit(trainingData);
        TrainingExample example = CreateTrainingExamples().First();
        TrainingExample[] examples =
        [
            example,
            example with { Title = "network packet" },
            example with { Body = "connection socket" },
            example with { FileNames = "socket.cs" },
            example with { FolderNames = "network" }
        ];

        var transformed = model.Transform(mlContext.Data.LoadFromEnumerable(examples));
        PipelineOutput[] results = mlContext.Data.CreateEnumerable<PipelineOutput>(transformed, reuseRowObject: false).ToArray();

        CollectionAssert.AreNotEqual(results[0].Features, results[1].Features);
        CollectionAssert.AreNotEqual(results[0].Features, results[2].Features);

        if (type == ModelType.PullRequest)
        {
            CollectionAssert.AreNotEqual(results[0].Features, results[3].Features);
            CollectionAssert.AreNotEqual(results[0].Features, results[4].Features);
        }
        else
        {
            CollectionAssert.AreEqual(results[0].Features, results[3].Features);
            CollectionAssert.AreEqual(results[0].Features, results[4].Features);
        }
    }

    private static IEnumerable<TrainingExample> CreateTrainingExamples()
        => Enumerable.Range(0, 20).Select(index => index % 2 == 0
            ? new TrainingExample
            {
                Label = "area-compiler",
                Title = "compiler syntax",
                Body = "language compilation",
                FileNames = "parser.cs",
                FolderNames = "compiler"
            }
            : new TrainingExample
            {
                Label = "area-network",
                Title = "network packet",
                Body = "connection socket",
                FileNames = "socket.cs",
                FolderNames = "network"
            });

    public sealed record TrainingExample
    {
        public string Label { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string FileNames { get; set; } = string.Empty;
        public string FolderNames { get; set; } = string.Empty;
    }

    public sealed class PipelineOutput
    {
        public uint LabelKey { get; set; }
        public float[] Features { get; set; } = [];
        public float[] Score { get; set; } = [];
        public string PredictedLabel { get; set; } = string.Empty;
    }
}
