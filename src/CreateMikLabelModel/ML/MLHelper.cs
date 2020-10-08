﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.ML;
using Microsoft.ML.Data;
using System;
using System.Diagnostics;

namespace CreateMikLabelModel.ML
{
    public class MLHelper
    {
        private readonly MLContext _mLContext;
        public MLHelper()
        {
            _mLContext = new MLContext(seed: 0);
        }

        public void Test(DataFilePaths files, bool forPrs)
        {
            MulticlassExperimentHelper.TestPrediction(_mLContext, files, forPrs: forPrs);
        }

        public void Train(DataFilePaths files, bool forPrs)
        {
            var stopWatch = Stopwatch.StartNew();

            var st = new ExperimentModifier(files, forPrs);
            Train(st);

            stopWatch.Stop();
            Trace.WriteLine($"Done creating model in {stopWatch.ElapsedMilliseconds}ms");
        }

        private void Train(ExperimentModifier settings)
        {
            var setup = MulticlassExperimentSettingsHelper.SetupExperiment(_mLContext, settings, settings.Paths, settings.ForPrs);

            try
            {
                // Start experiment
                var textLoader = _mLContext.Data.CreateTextLoader(setup.columnInference.TextLoaderOptions);
                var paths = settings.Paths;

                // train once:
                var experimentResult = MulticlassExperimentHelper.Train(
                    _mLContext, settings.LabelColumnName, setup.experimentSettings, new MulticlassExperimentProgressHandler(), paths, textLoader);

                // train twice
                var refitModel = MulticlassExperimentHelper.Retrain(experimentResult,
                    "refit model",
                    new MultiFileSource(paths.TrainPath, paths.ValidatePath),
                    paths.ValidatePath,
                    paths.FittedModelPath, textLoader, _mLContext);

                // final train:
                refitModel = MulticlassExperimentHelper.Retrain(_mLContext, experimentResult, setup.columnInference, paths);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
            }
        }
    }
}
