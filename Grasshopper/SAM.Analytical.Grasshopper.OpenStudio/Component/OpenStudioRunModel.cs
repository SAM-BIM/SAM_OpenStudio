// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Core.Grasshopper;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.Grasshopper.OpenStudio
{
    public class OpenStudioRunModel : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("2e9d5b40-6c17-4f8a-b3e2-95a4d0c8f176");

        /// <summary>
        /// The latest version of this component
        /// </summary>
        public override string LatestComponentVersion => "1.0.0";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.SAM_OpenStudio;

        /// <summary>
        /// Runs an existing OpenStudio model (OSM or OSW) through the OpenStudio CLI and returns
        /// paths, diagnostics and annual Ideal Loads energy. Thin wrapper over
        /// SAM.Analytical.OpenStudio.OpenStudioSimulationRunner — no run logic lives here.
        /// </summary>
        public OpenStudioRunModel()
          : base("OpenStudio.RunModel", "OpenStudio.RunModel",
              "Runs an existing OpenStudio model (OSM or OSW) through the OpenStudio CLI (EnergyPlus)",
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
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "_path", NickName = "_path", Description = "OSM or OSW file path", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "epwPath_", NickName = "epwPath_", Description = "EPW weather file path (required for OSM input)", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "outputDirectory_", NickName = "outputDirectory_", Description = "Directory for the generated OSW and run folder (defaults next to the input)", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Boolean param_Boolean = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_run", NickName = "_run", Description = "True executes the simulation", Access = GH_ParamAccess.item };
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
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "successful", NickName = "successful", Description = "True when the CLI exited 0 with no fatal errors and results exist", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "sqlPath", NickName = "sqlPath", Description = "EnergyPlus SQLite results path", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "heating", NickName = "heating", Description = "Annual Ideal Loads heating energy [kWh]", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "cooling", NickName = "cooling", Description = "Annual Ideal Loads cooling energy [kWh]", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "diagnostics", NickName = "diagnostics", Description = "Run diagnostics", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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

            bool run = false;
            index = Params.IndexOfInputParam("_run");
            if (index != -1)
            {
                dataAccess.GetData(index, ref run);
            }

            if (!run)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Set _run to True to execute the simulation");
                return;
            }

            string path = null;
            index = Params.IndexOfInputParam("_path");
            if (index == -1 || !dataAccess.GetData(index, ref path) || string.IsNullOrWhiteSpace(path))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            string epwPath = null;
            index = Params.IndexOfInputParam("epwPath_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref epwPath);
            }

            string outputDirectory = null;
            index = Params.IndexOfInputParam("outputDirectory_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref outputDirectory);
            }

            List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = new List<Core.OpenStudio.OpenStudioDiagnostic>();
            Core.OpenStudio.OpenStudioRunResult openStudioRunResult = Analytical.OpenStudio.OpenStudioSimulationRunner.Run(path, epwPath, outputDirectory, null, out Analytical.OpenStudio.OpenStudioLoadSummary openStudioLoadSummary, diagnostics);

            index = Params.IndexOfOutputParam("successful");
            if (index != -1)
            {
                dataAccess.SetData(index, openStudioRunResult.Success);
            }

            index = Params.IndexOfOutputParam("sqlPath");
            if (index != -1)
            {
                dataAccess.SetData(index, openStudioRunResult.SqlPath);
            }

            index = Params.IndexOfOutputParam("heating");
            if (index != -1 && openStudioLoadSummary != null)
            {
                dataAccess.SetData(index, openStudioLoadSummary.TotalHeating);
            }

            index = Params.IndexOfOutputParam("cooling");
            if (index != -1 && openStudioLoadSummary != null)
            {
                dataAccess.SetData(index, openStudioLoadSummary.TotalCooling);
            }

            index = Params.IndexOfOutputParam("diagnostics");
            if (index != -1)
            {
                dataAccess.SetDataList(index, diagnostics.ConvertAll(x => x.ToString()));
            }
        }
    }
}
