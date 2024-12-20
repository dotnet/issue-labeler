﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.ML.AutoML;
using Microsoft.ML.Data;
using System.Diagnostics;
using System.Text;

namespace ModelCreator;

public static class ConsoleHelper
{
    private const int Width = 114;

    internal static void PrintIterationMetrics(int iteration, string trainerName, MulticlassClassificationMetrics metrics, double? runtimeInSeconds)
    {
        CreateRow($"{iteration,-4} {trainerName,-35} {metrics?.MicroAccuracy ?? double.NaN,14:F4} {metrics?.MacroAccuracy ?? double.NaN,14:F4} {runtimeInSeconds,9:F1}", Width);
    }

    internal static void PrintIterationException(Exception ex)
    {
        Trace.WriteLine($"Exception during AutoML iteration: {ex}");
    }

    internal static void PrintMulticlassClassificationMetricsHeader()
    {
        CreateRow($"{"",-4} {"Trainer",-35} {"MicroAccuracy",14} {"MacroAccuracy",14} {"Duration",9}", Width);
    }

    private static void CreateRow(string message, int width)
    {
        Trace.WriteLine("|" + message.PadRight(width - 2) + "|");
    }

    public static void ConsoleWriteHeader(params string[] lines)
    {
        var defaultColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Trace.WriteLine(" ");
        foreach (var line in lines)
        {
            Trace.WriteLine(line);
        }
        var maxLength = lines.Select(x => x.Length).Max();
        Trace.WriteLine(new string('#', maxLength));
        Console.ForegroundColor = defaultColor;
    }

    public static string BuildStringTable(IList<string[]> arrValues)
    {
        var maxColumnsWidth = GetMaxColumnsWidth(arrValues);
        var headerSpliter = new string('-', maxColumnsWidth.Sum(i => i + 3) - 1);

        var sb = new StringBuilder();
        for (var rowIndex = 0; rowIndex < arrValues.Count; rowIndex++)
        {
            if (rowIndex == 0)
            {
                sb.AppendFormat("  {0} ", headerSpliter);
                sb.AppendLine();
            }

            for (var colIndex = 0; colIndex < arrValues[0].Length; colIndex++)
            {
                // Print cell
                var cell = arrValues[rowIndex][colIndex];
                cell = cell.PadRight(maxColumnsWidth[colIndex]);
                sb.Append(" | ");
                sb.Append(cell);
            }

            // Print end of line
            sb.Append(" | ");
            sb.AppendLine();

            // Print splitter
            if (rowIndex == 0)
            {
                sb.AppendFormat(" |{0}| ", headerSpliter);
                sb.AppendLine();
            }

            if (rowIndex == arrValues.Count - 1)
            {
                sb.AppendFormat("  {0} ", headerSpliter);
            }
        }

        return sb.ToString();
    }

    private static int[] GetMaxColumnsWidth(IList<string[]> arrValues)
    {
        var maxColumnsWidth = new int[arrValues[0].Length];
        for (var colIndex = 0; colIndex < arrValues[0].Length; colIndex++)
        {
            for (var rowIndex = 0; rowIndex < arrValues.Count; rowIndex++)
            {
                var newLength = arrValues[rowIndex][colIndex].Length;
                var oldLength = maxColumnsWidth[colIndex];

                if (newLength > oldLength)
                {
                    maxColumnsWidth[colIndex] = newLength;
                }
            }
        }

        return maxColumnsWidth;
    }

    private class ColumnInferencePrinter
    {
        private static readonly string[] TableHeaders = new[] { "Name", "Data Type", "Purpose" };

        private readonly ColumnInferenceResults _results;

        public ColumnInferencePrinter(ColumnInferenceResults results)
        {
            _results = results;
        }

        public void Print()
        {
            var tableRows = new List<string[]>();

            // Add headers
            tableRows.Add(TableHeaders);

            // Add column data
            var info = _results.ColumnInformation;
            AppendTableRow(tableRows, info.LabelColumnName, "Label");
            AppendTableRow(tableRows, info.ExampleWeightColumnName, "Weight");
            AppendTableRow(tableRows, info.SamplingKeyColumnName, "Sampling Key");
            AppendTableRows(tableRows, info.CategoricalColumnNames, "Categorical");
            AppendTableRows(tableRows, info.NumericColumnNames, "Numeric");
            AppendTableRows(tableRows, info.TextColumnNames, "Text");
            AppendTableRows(tableRows, info.IgnoredColumnNames, "Ignored");

            Trace.WriteLine(BuildStringTable(tableRows));
        }

        private void AppendTableRow(ICollection<string[]> tableRows,
            string columnName, string columnPurpose)
        {
            if (columnName == null)
            {
                return;
            }

            tableRows.Add(new[]
            {
                columnName,
                GetColumnDataType(columnName),
                columnPurpose
            });
        }

        private void AppendTableRows(ICollection<string[]> tableRows,
            IEnumerable<string> columnNames, string columnPurpose)
        {
            foreach (var columnName in columnNames)
            {
                AppendTableRow(tableRows, columnName, columnPurpose);
            }
        }

        private string GetColumnDataType(string columnName)
        {
            return _results.TextLoaderOptions.Columns.First(c => c.Name == columnName).DataKind.ToString();
        }
    }
}
