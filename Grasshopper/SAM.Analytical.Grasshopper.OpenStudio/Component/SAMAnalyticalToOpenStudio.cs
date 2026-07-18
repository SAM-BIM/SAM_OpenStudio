// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Analytical.Grasshopper;
using SAM.Core.Grasshopper;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.Grasshopper.OpenStudio
{
    public class SAMAnalyticalToOpenStudio : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("7f3a2c91-4b6e-4d28-9c5a-8e1f0b7d3a64");

        /// <summary>
        /// The latest version of this component
        /// </summary>
        public override string LatestComponentVersion => "1.0.0";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.SAM_OpenStudio;

        /// <summary>
        /// Converts a SAM AnalyticalModel to an OpenStudio model (OSM + OSW) and optionally runs
        /// the annual EnergyPlus Ideal Loads simulation through the OpenStudio CLI. Thin wrapper
        /// over SAM.Analytical.OpenStudio.Convert.ToOpenStudio — no conversion rules live here.
        /// </summary>
        public SAMAnalyticalToOpenStudio()
          : base("SAMAnalytical.ToOpenStudio", "SAMAnalytical.ToOpenStudio",
              "Converts SAM AnalyticalModel to OpenStudio model (OSM/OSW) and optionally runs the annual EnergyPlus Ideal Loads simulation",
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
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "_epwPath", NickName = "_epwPath", Description = "EPW weather file path", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "_outputDirectory", NickName = "_outputDirectory", Description = "Directory for the OSM/OSW and the simulation run folder", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Boolean param_Boolean = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_run", NickName = "_run", Description = "True executes the OpenStudio CLI simulation; False converts and saves OSM/OSW only", Access = GH_ParamAccess.item };
                param_Boolean.SetPersistentData(false);
                result.Add(new GH_SAMParam(param_Boolean, ParamVisibility.Binding));

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

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="dataAccess">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            int index;

            AnalyticalModel analyticalModel = null;
            index = Params.IndexOfInputParam("_analyticalModel");
            if (index == -1 || !dataAccess.GetData(index, ref analyticalModel) || analyticalModel == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            string epwPath = null;
            index = Params.IndexOfInputParam("_epwPath");
            if (index == -1 || !dataAccess.GetData(index, ref epwPath) || string.IsNullOrWhiteSpace(epwPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            string outputDirectory = null;
            index = Params.IndexOfInputParam("_outputDirectory");
            if (index == -1 || !dataAccess.GetData(index, ref outputDirectory) || string.IsNullOrWhiteSpace(outputDirectory))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            bool run = false;
            index = Params.IndexOfInputParam("_run");
            if (index != -1)
            {
                dataAccess.GetData(index, ref run);
            }

            Analytical.OpenStudio.OpenStudioConversionResult openStudioConversionResult = Analytical.OpenStudio.Convert.ToOpenStudio(analyticalModel, epwPath, outputDirectory, null, null, run);
            if (openStudioConversionResult == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Conversion failed");
                return;
            }

            index = Params.IndexOfOutputParam("osmPath");
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
                bool successful = openStudioConversionResult.IsValid && (!run || (openStudioConversionResult.RunResult != null && openStudioConversionResult.RunResult.Success));
                dataAccess.SetData(index, successful);
            }
        }
    }
}
