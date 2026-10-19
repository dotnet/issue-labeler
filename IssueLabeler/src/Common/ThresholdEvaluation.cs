// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace IssueLabeler.Evaluation;

public sealed record AreaEvaluation(string Label, int TrainingSupport, int HeldOutSupport, int Correct, int Wrong, int Abstained, int Misrouted)
{
    public int Missed => Abstained + Misrouted;
    public double? Precision => Correct + Wrong == 0 ? null : (double)Correct / (Correct + Wrong);
    public double? Coverage => HeldOutSupport == 0 ? null : (double)(HeldOutSupport - Abstained) / HeldOutSupport;
}

public sealed record ThresholdResult(float Threshold, int Total, int Correct, int Wrong, int Abstained, AreaEvaluation[] Areas)
{
    public int Accepted => Correct + Wrong;
    public double? Precision => Accepted == 0 ? null : (double)Correct / Accepted;
    public double Coverage => (double)Accepted / Total;
    public double? PrecisionLower => Wilson(Correct, Accepted)?.Lower;
    public double? PrecisionUpper => Wilson(Correct, Accepted)?.Upper;

    public static (double Lower, double Upper)? Wilson(int successes, int count)
    {
        if (count < 0 || successes < 0 || successes > count)
        {
            throw new ArgumentOutOfRangeException(nameof(successes), "Successes must be between zero and the sample count.");
        }

        if (count == 0)
        {
            return null;
        }

        const double z = 1.959963984540054;
        double proportion = (double)successes / count;
        double denominator = 1 + z * z / count;
        double center = (proportion + z * z / (2 * count)) / denominator;
        double margin = z * Math.Sqrt(proportion * (1 - proportion) / count + z * z / (4.0 * count * count)) / denominator;
        return (Math.Max(0, center - margin), Math.Min(1, center + margin));
    }
}

public static class ThresholdEvaluation
{
    public static ThresholdResult[] Evaluate(EvaluationArtifact artifact, float[] thresholds, Func<string, bool> labelPredicate)
    {
        ValidatePopulation(artifact, labelPredicate);
        if (thresholds.Length == 0 || thresholds.Any(value => !float.IsFinite(value) || value < 0 || value > 1))
        {
            throw new ArgumentException("At least one finite threshold between 0 and 1 is required.", nameof(thresholds));
        }

        var predictions = GetPredictions(artifact);
        string[] labels = [.. artifact.Labels.Concat(artifact.Rows.Select(row => row.Label))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        var trainingSupport = artifact.TrainingSupport
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(pair => pair.Value), StringComparer.OrdinalIgnoreCase);
        var results = new List<ThresholdResult>();

        foreach (float threshold in thresholds.Distinct().Order())
        {
            int correct = 0;
            int wrong = 0;
            int abstained = 0;
            var counts = labels.ToDictionary(label => label, _ => new AreaCounts(), StringComparer.OrdinalIgnoreCase);
            foreach (var (row, prediction) in predictions)
            {
                counts[row.Label].Support++;
                if (prediction.Score < threshold)
                {
                    abstained++;
                    counts[row.Label].Abstained++;
                }
                else if (prediction.Label.Equals(row.Label, StringComparison.OrdinalIgnoreCase))
                {
                    correct++;
                    counts[row.Label].Correct++;
                }
                else
                {
                    wrong++;
                    counts[row.Label].Misrouted++;
                    counts[prediction.Label].Wrong++;
                }
            }

            AreaEvaluation[] areas = [.. labels.Select(label => new AreaEvaluation(
                label, trainingSupport.GetValueOrDefault(label), counts[label].Support, counts[label].Correct, counts[label].Wrong,
                counts[label].Abstained, counts[label].Misrouted))];
            results.Add(new(threshold, artifact.Rows.Length, correct, wrong, abstained, areas));
        }

        return [.. results];
    }

    private static void ValidatePopulation(EvaluationArtifact artifact, Func<string, bool> labelPredicate)
    {
        artifact.Validate();
        if (!artifact.Labels.All(labelPredicate) || artifact.Rows.Any(row => !labelPredicate(row.Label)))
        {
            throw new ArgumentException("The label prefix must include every model and reference label; a partial prefix would change the evaluation population.", nameof(labelPredicate));
        }
    }

    private static (EvaluationRow Row, (string Label, float Score) Prediction)[] GetPredictions(EvaluationArtifact artifact) =>
        artifact.Rows.Select(row =>
        {
            int index = Array.IndexOf(row.Scores, row.Scores.Max());
            return (Row: row, Prediction: (Label: artifact.Labels[index], Score: row.Scores[index]));
        }).ToArray();

    private sealed class AreaCounts
    {
        public int Support { get; set; }
        public int Correct { get; set; }
        public int Wrong { get; set; }
        public int Abstained { get; set; }
        public int Misrouted { get; set; }
    }
}
