﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.ML.AutoML;
using Microsoft.ML.Data;

namespace ModelCreator.ML;

/// <summary>
/// Progress handler that AutoML will invoke after each model it produces and evaluates.
/// </summary>
public class MulticlassExperimentProgressHandler : IProgress<RunDetail<MulticlassClassificationMetrics>>
{
    private int _iterationIndex;

    public void Report(RunDetail<MulticlassClassificationMetrics> iterationResult)
    {
        if (_iterationIndex++ == 0)
        {
            ConsoleHelper.PrintMulticlassClassificationMetricsHeader();
        }

        if (iterationResult.Exception != null)
        {
            ConsoleHelper.PrintIterationException(iterationResult.Exception);
        }
        else
        {
            ConsoleHelper.PrintIterationMetrics(_iterationIndex, iterationResult.TrainerName,
                iterationResult.ValidationMetrics, iterationResult.RuntimeInSeconds);
        }
    }
}
