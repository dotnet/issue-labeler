// Licensed to the .NET Foundation under one or more agreements.
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
            try
            {
                MulticlassExperimentHelper.TestPrediction(_mLContext, files, forPrs: forPrs);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ERROR: Failed to test model. Skipping.");
                Trace.WriteLine(ex.ToString());
            }
        }

        public void Train(DataFilePaths files, bool forPrs)
        {
            var stopWatch = Stopwatch.StartNew();

            try
            {
                var st = new ExperimentModifier(files, forPrs);
                Train(st);

                stopWatch.Stop();
                Trace.WriteLine($"Done creating model in {stopWatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                stopWatch.Stop();
                Trace.WriteLine($"ERROR: Failed to create model after {stopWatch.ElapsedMilliseconds}ms. Skipping.");
                Trace.WriteLine(ex.ToString());
            }
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
                    _mLContext, setup.experimentSettings, new MulticlassExperimentProgressHandler(), paths, textLoader, setup.columnInference);

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
