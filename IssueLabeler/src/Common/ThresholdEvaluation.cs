// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text;
using System.Text.Json;

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
    public static float[] ParseThresholds(string value)
    {
        string[] tokens = value.Split(',');
        if (tokens.Length is 0 or > 50)
        {
            throw new ArgumentException("Provide between 1 and 50 comma-separated thresholds.");
        }

        var thresholds = new List<float>();
        foreach (string token in tokens)
        {
            if (!float.TryParse(token.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float threshold) ||
                !float.IsFinite(threshold) || threshold < 0 || threshold > 1)
            {
                throw new ArgumentException($"Invalid threshold '{token}'. Use invariant decimal values between 0 and 1.");
            }

            thresholds.Add(threshold);
        }

        return [.. thresholds.Distinct().Order()];
    }

    public static ThresholdResult[] Evaluate(EvaluationArtifact artifact, float[] thresholds, Func<string, bool> labelPredicate)
    {
        artifact.Validate();
        if (thresholds.Length == 0 || thresholds.Any(value => !float.IsFinite(value) || value < 0 || value > 1))
        {
            throw new ArgumentException("At least one finite threshold between 0 and 1 is required.", nameof(thresholds));
        }

        if (!artifact.Labels.All(labelPredicate) || artifact.Rows.Any(row => !labelPredicate(row.Label)))
        {
            throw new ArgumentException("The label prefix must include every model and reference label; a partial prefix would change the evaluation population.", nameof(labelPredicate));
        }

        var predictions = artifact.Rows.Select(row =>
        {
            int index = Array.IndexOf(row.Scores, row.Scores.Max());
            return (Row: row, Prediction: (Label: artifact.Labels[index], Score: row.Scores[index]));
        }).ToArray();
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

    public static string Markdown(EvaluationArtifact artifact, ThresholdResult[] results, string labelPrefix, float? referenceThreshold = null)
    {
        var text = new StringBuilder();
        text.AppendLine("## Held-out threshold evaluation");
        text.AppendLine();
        text.AppendLine($"Model: `{artifact.ModelSha256}`");
        text.AppendLine($"Dataset: `{artifact.DatasetSha256}`");
        text.AppendLine($"Tool: `{artifact.ToolVersion}`");
        text.AppendLine($"Evidence created: `{artifact.CreatedAtUtc:O}`");
        if (referenceThreshold is float reference)
        {
            text.AppendLine($"Reference threshold: **{Number(reference)}** (comparison only; not changed).");
        }
        text.AppendLine();
        text.AppendLine($"Training examples: **{artifact.TrainingCount:N0}**. Held-out examples: **{artifact.Rows.Length:N0}**. Label prefix: `{labelPrefix}`.");
        text.AppendLine("Top-1 model-level evaluation: one label is accepted when its score is **greater than or equal to** the threshold.");
        text.AppendLine("These are the saved predictions from a single random 20% split. Once used to select a threshold, this is a tuning set, not an untouched final test.");
        text.AppendLine();
        text.AppendLine("| Threshold | Correct accepted | Wrong accepted | Abstained | Accepted precision | Precision 95% interval | Coverage |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach (var result in results)
        {
            string interval = result.PrecisionLower is double lower && result.PrecisionUpper is double upper
                ? $"{Percent(lower)} - {Percent(upper)}" : "N/A";
            text.AppendLine($"| {Number(result.Threshold)} | {result.Correct} | {result.Wrong} | {result.Abstained} | {Percent(result.Precision)} | {interval} | {Percent(result.Coverage)} |");
        }

        text.AppendLine();
        text.AppendLine("Precision is correct / accepted; coverage is accepted / all. N/A means no predictions were accepted. Scores are not guaranteed correctness probabilities.");
        text.AppendLine("Intervals are pointwise Wilson intervals, not guarantees for a threshold chosen after searching this table. Small or dependent samples weaken the evidence.");
        text.AppendLine("No model was retrained, no issues were downloaded, and no production threshold was changed.");
        text.AppendLine();
        var areaResult = results.FirstOrDefault(result => result.Threshold == referenceThreshold) ?? results[0];
        text.AppendLine($"### Per-area evidence at threshold {Number(areaResult.Threshold)}");
        text.AppendLine();
        text.AppendLine("| Area | Training support | Held-out support | Correct | Wrong assigned here | Abstained | Routed elsewhere | Precision | Coverage |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var area in areaResult.Areas)
        {
            text.AppendLine($"| {EscapeMarkdown(area.Label)} | {area.TrainingSupport} | {area.HeldOutSupport} | {area.Correct} | {area.Wrong} | {area.Abstained} | {area.Misrouted} | {Percent(area.Precision)} | {Percent(area.Coverage)} |");
        }

        text.AppendLine();
        text.AppendLine("Per-area precision uses assignments to that area; coverage uses reference examples from that area. Zero or small support is insufficient evidence for reliable per-area decisions. Counts for every threshold are included in the report files.");
        return text.ToString();
    }

    public static void WriteReports(string directory, EvaluationArtifact artifact, ThresholdResult[] results, string labelPrefix, float? referenceThreshold = null)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "summary.md"), Markdown(artifact, results, labelPrefix, referenceThreshold));
        using (var stream = File.Create(Path.Combine(directory, "scorecard.json")))
        {
            JsonSerializer.Serialize(stream, new
            {
                artifact.SchemaVersion,
                artifact.ModelSha256,
                artifact.DatasetSha256,
                artifact.ToolVersion,
                artifact.ModelType,
                artifact.CreatedAtUtc,
                artifact.TrainingCount,
                HeldOutCount = artifact.Rows.Length,
                LabelPrefix = labelPrefix,
                ReferenceThreshold = referenceThreshold,
                Results = results
            }, new JsonSerializerOptions(EvaluationArtifact.JsonOptions) { WriteIndented = true });
        }

        var areas = new StringBuilder("threshold,label,trainingSupport,heldOutSupport,truePositives,falsePositives,falseNegatives,abstained,misrouted,precision,coverage\r\n");
        foreach (var result in results)
        {
            foreach (var area in result.Areas)
            {
                string label = $"\"{area.Label.Replace("\"", "\"\"")}\"";
                areas.Append(FormattableString.Invariant($"{result.Threshold:R},{label},{area.TrainingSupport},{area.HeldOutSupport},{area.Correct},{area.Wrong},{area.Missed},{area.Abstained},{area.Misrouted},{area.Precision?.ToString("R", CultureInfo.InvariantCulture)},{area.Coverage?.ToString("R", CultureInfo.InvariantCulture)}")).Append("\r\n");
            }
        }

        File.WriteAllText(Path.Combine(directory, "per-area.csv"), areas.ToString());
    }

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Percent(double? value) => value?.ToString("P2", CultureInfo.InvariantCulture) ?? "N/A";
    private static string EscapeMarkdown(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private sealed class AreaCounts
    {
        public int Support { get; set; }
        public int Correct { get; set; }
        public int Wrong { get; set; }
        public int Abstained { get; set; }
        public int Misrouted { get; set; }
    }
}
