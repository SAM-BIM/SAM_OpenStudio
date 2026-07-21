// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Core.Grasshopper;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SAM.Analytical.Grasshopper.OpenStudio
{
    public class SAMAnalyticalToOpenStudio : GH_SAMAsyncComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("7f3a2c91-4b6e-4d28-9c5a-8e1f0b7d3a64");

        /// <summary>
        /// The latest version of this component
        /// </summary>
        public override string LatestComponentVersion => "1.2.0";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.SAM_OpenStudio;

        /// <summary>
        /// Converts a SAM AnalyticalModel to an OpenStudio model (OSM + OSW) and optionally runs
        /// the annual EnergyPlus Ideal Loads simulation through the OpenStudio CLI. Non-blocking
        /// (C6): the simulation executes on a background task with cancellation; results are
        /// harvested on the UI thread. Thin wrapper over SAM.Analytical.OpenStudio.Convert —
        /// no conversion rules live here. Weather/design-day source precedence: explicit
        /// _epwPath/ddyPath_ override the WeatherData and design days embedded in the model.
        /// </summary>
        public SAMAnalyticalToOpenStudio()
          : base("SAMAnalytical.ToOpenStudio", "SAMAnalytical.ToOpenStudio",
              "Converts SAM AnalyticalModel to OpenStudio model (OSM/OSW) and optionally runs the annual EnergyPlus Ideal Loads simulation (asynchronous, cancellable)",
              "SAM", "OpenStudio")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooAnalyticalModelParam() { Name = "_analyticalModel", NickName = "_analyticalModel", Description = "SAM AnalyticalModel", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "_epwPath", NickName = "_epwPath", Description = "EPW weather file path (optional — when empty the WeatherData embedded in the AnalyticalModel is used)", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "_outputDirectory", NickName = "_outputDirectory", Description = "Directory for the OSM/OSW and the simulation run folder", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Boolean param_Boolean = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_run", NickName = "_run", Description = "True executes the OpenStudio CLI simulation; False converts and saves OSM/OSW only", Access = GH_ParamAccess.item };
                param_Boolean.SetPersistentData(false);
                result.Add(new GH_SAMParam(param_Boolean, ParamVisibility.Binding));

                result.Add(CreateCancelParam());

                // Appended last on purpose: Grasshopper pairs archived inputs with registered
                // inputs by position, so inserting ddyPath_ earlier would mis-wire existing
                // documents (their _outputDirectory/_run sources would shift one position).
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "ddyPath_", NickName = "ddyPath_", Description = "DDY design-day file path (optional — overrides the design days embedded in the AnalyticalModel)", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));

                return result.ToArray();
            }
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override GH_SAMParam[] Outputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "osmPath", NickName = "osmPath", Description = "Saved OpenStudio model path", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "oswPath", NickName = "oswPath", Description = "Generated OpenStudio workflow path", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "sqlPath", NickName = "sqlPath", Description = "EnergyPlus SQLite results path (after a run)", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "heating", NickName = "heating", Description = "Annual Ideal Loads heating energy [kWh]", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "cooling", NickName = "cooling", Description = "Annual Ideal Loads cooling energy [kWh]", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "diagnostics", NickName = "diagnostics", Description = "Conversion and simulation diagnostics", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "successful", NickName = "successful", Description = "True when the conversion (and run, when requested) succeeded", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                return result.ToArray();
            }
        }

        protected override string ComputeSignature(IGH_DataAccess dataAccess)
        {
            AnalyticalModel analyticalModel = null;
            int index = Params.IndexOfInputParam("_analyticalModel");
            if (index == -1 || !dataAccess.GetData(index, ref analyticalModel) || analyticalModel == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid _analyticalModel data");
                return null;
            }

            string epwPath = null;
            index = Params.IndexOfInputParam("_epwPath");
            if (index != -1)
            {
                dataAccess.GetData(index, ref epwPath);
            }

            string outputDirectory = null;
            index = Params.IndexOfInputParam("_outputDirectory");
            if (index == -1 || !dataAccess.GetData(index, ref outputDirectory) || string.IsNullOrWhiteSpace(outputDirectory))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid _outputDirectory data");
                return null;
            }

            bool run = false;
            index = Params.IndexOfInputParam("_run");
            if (index != -1)
            {
                dataAccess.GetData(index, ref run);
            }

            string ddyPath = null;
            index = Params.IndexOfInputParam("ddyPath_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref ddyPath);
            }

            // Embedded weather/design-day content is part of the signature: changing it on the
            // same AnalyticalModel reference (same Guid) must trigger a new conversion.
            string embeddedFingerprint = Analytical.OpenStudio.Query.EmbeddedWeatherFingerprint(analyticalModel);

            return string.Format("{0:N}|{1}|{2}|{3}|{4}|{5}", analyticalModel.Guid, epwPath ?? string.Empty, outputDirectory, run, ddyPath ?? string.Empty, embeddedFingerprint ?? string.Empty);
        }

        protected override Task CreateTask(IGH_DataAccess dataAccess, CancellationToken cancellationToken)
        {
            AnalyticalModel analyticalModel = null;
            dataAccess.GetData(Params.IndexOfInputParam("_analyticalModel"), ref analyticalModel);

            string epwPath = null;
            dataAccess.GetData(Params.IndexOfInputParam("_epwPath"), ref epwPath);

            string outputDirectory = null;
            dataAccess.GetData(Params.IndexOfInputParam("_outputDirectory"), ref outputDirectory);

            bool run = false;
            dataAccess.GetData(Params.IndexOfInputParam("_run"), ref run);

            string ddyPath = null;
            int index = Params.IndexOfInputParam("ddyPath_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref ddyPath);
            }

            Core.OpenStudio.OpenStudioConversionOptions openStudioConversionOptions = new Core.OpenStudio.OpenStudioConversionOptions();
            if (!string.IsNullOrWhiteSpace(ddyPath))
            {
                openStudioConversionOptions.DdyPath = ddyPath;
            }

            return Analytical.OpenStudio.Convert.ToOpenStudioAsync(analyticalModel, epwPath, outputDirectory, openStudioConversionOptions, null, run, null, cancellationToken);
        }

        protected override void Harvest(Task task, IGH_DataAccess dataAccess)
        {
            if (task.IsFaulted)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, task.Exception?.GetBaseException().Message ?? "Simulation failed");
                return;
            }

            Analytical.OpenStudio.OpenStudioConversionResult openStudioConversionResult = ((Task<Analytical.OpenStudio.OpenStudioConversionResult>)task).Result;
            if (openStudioConversionResult == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Conversion failed");
                return;
            }

            using (openStudioConversionResult)
            {
                int index = Params.IndexOfOutputParam("osmPath");
                if (index != -1)
                {
                    dataAccess.SetData(index, openStudioConversionResult.OsmPath);
                }

                index = Params.IndexOfOutputParam("oswPath");
                if (index != -1)
                {
                    dataAccess.SetData(index, openStudioConversionResult.OswPath);
                }

                index = Params.IndexOfOutputParam("sqlPath");
                if (index != -1)
                {
                    dataAccess.SetData(index, openStudioConversionResult.RunResult?.SqlPath);
                }

                index = Params.IndexOfOutputParam("heating");
                if (index != -1 && openStudioConversionResult.Loads != null)
                {
                    dataAccess.SetData(index, openStudioConversionResult.Loads.TotalHeating);
                }

                index = Params.IndexOfOutputParam("cooling");
                if (index != -1 && openStudioConversionResult.Loads != null)
                {
                    dataAccess.SetData(index, openStudioConversionResult.Loads.TotalCooling);
                }

                index = Params.IndexOfOutputParam("diagnostics");
                if (index != -1)
                {
                    List<string> diagnostics = new List<string>();
                    foreach (Core.OpenStudio.OpenStudioDiagnostic openStudioDiagnostic in openStudioConversionResult.Diagnostics)
                    {
                        diagnostics.Add(openStudioDiagnostic.ToString());
                    }

                    dataAccess.SetDataList(index, diagnostics);
                }

                index = Params.IndexOfOutputParam("successful");
                if (index != -1)
                {
                    bool run = false;
                    int runIndex = Params.IndexOfInputParam("_run");
                    if (runIndex != -1)
                    {
                        dataAccess.GetData(runIndex, ref run);
                    }

                    bool successful = openStudioConversionResult.IsValid && (!run || (openStudioConversionResult.RunResult != null && openStudioConversionResult.RunResult.Success));
                    dataAccess.SetData(index, successful);
                }
            }
        }
    }
}
