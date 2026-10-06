// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Core.Grasshopper;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SAM.Analytical.Grasshopper.OpenStudio
{
    public class OpenStudioRunModel : GH_SAMAsyncComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("2e9d5b40-6c17-4f8a-b3e2-95a4d0c8f176");

        /// <summary>
        /// The latest version of this component
        /// </summary>
        public override string LatestComponentVersion => "1.2.0";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.SAM_OpenStudio;

        /// <summary>
        /// Runs an existing OpenStudio model (OSM or OSW) through the OpenStudio CLI and returns
        /// paths, diagnostics and annual Ideal Loads energy. Non-blocking (C6): the simulation
        /// executes on a background task with cancellation; results are harvested on the UI
        /// thread. Thin wrapper over SAM.Analytical.OpenStudio.OpenStudioSimulationRunner — no
        /// run logic lives here.
        /// </summary>
        public OpenStudioRunModel()
          : base("OpenStudio.RunModel", "OpenStudio.RunModel",
              "Runs an existing OpenStudio model (OSM or OSW) through the OpenStudio CLI (EnergyPlus) — asynchronous, cancellable",
              "SAM", "OpenStudio")
        {
        }

        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);

            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "Go to Directory", Menu_GoToDirectory, Properties.Resources.SAM_Small, true, false);
        }

        void Menu_GoToDirectory(object sender, EventArgs e)
        {
            string outputDirectory = MenuHelper.GetVolatileString(this, "outputDirectory_");
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                MenuHelper.GoToDirectory(outputDirectory, true);
                return;
            }

            MenuHelper.GoToFileDirectory(MenuHelper.GetVolatileString(this, "_path"));
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

                result.Add(CreateCancelParam());

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
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "sqlPath", NickName = "sqlPath", Description = "EnergyPlus SQLite results path", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "heating", NickName = "heating", Description = "Annual Ideal Loads heating energy [kWh]", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "cooling", NickName = "cooling", Description = "Annual Ideal Loads cooling energy [kWh]", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "diagnostics", NickName = "diagnostics", Description = "Run diagnostics", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "successful", NickName = "successful", Description = "True when the CLI exited 0 with no fatal errors and results exist", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                return result.ToArray();
            }
        }

        protected override string ComputeSignature(IGH_DataAccess dataAccess)
        {
            bool run = false;
            int index = Params.IndexOfInputParam("_run");
            if (index != -1)
            {
                dataAccess.GetData(index, ref run);
            }

            if (!run)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Set _run to True to execute the simulation");
                return null;
            }

            string path = null;
            index = Params.IndexOfInputParam("_path");
            if (index == -1 || !dataAccess.GetData(index, ref path) || string.IsNullOrWhiteSpace(path))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid _path data");
                return null;
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

            return string.Format("{0}|{1}|{2}", path, epwPath, outputDirectory);
        }

        protected override Task CreateTask(IGH_DataAccess dataAccess, CancellationToken cancellationToken)
        {
            string path = null;
            dataAccess.GetData(Params.IndexOfInputParam("_path"), ref path);

            string epwPath = null;
            dataAccess.GetData(Params.IndexOfInputParam("epwPath_"), ref epwPath);

            string outputDirectory = null;
            dataAccess.GetData(Params.IndexOfInputParam("outputDirectory_"), ref outputDirectory);

            return Task.Run(() =>
            {
                List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = new List<Core.OpenStudio.OpenStudioDiagnostic>();
                Core.OpenStudio.OpenStudioRunResult openStudioRunResult = Analytical.OpenStudio.OpenStudioSimulationRunner.Run(path, epwPath, outputDirectory, null, out Analytical.OpenStudio.OpenStudioLoadSummary openStudioLoadSummary, diagnostics, cancellationToken);
                return new RunOutcome(openStudioRunResult, openStudioLoadSummary, diagnostics);
            }, cancellationToken);
        }

        protected override void Harvest(Task task, IGH_DataAccess dataAccess)
        {
            if (task.IsFaulted)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, task.Exception?.GetBaseException().Message ?? "Simulation failed");
                return;
            }

            RunOutcome runOutcome = ((Task<RunOutcome>)task).Result;

            int index = Params.IndexOfOutputParam("successful");
            if (index != -1)
            {
                dataAccess.SetData(index, runOutcome.RunResult.Success);
            }

            index = Params.IndexOfOutputParam("sqlPath");
            if (index != -1)
            {
                dataAccess.SetData(index, runOutcome.RunResult.SqlPath);
            }

            index = Params.IndexOfOutputParam("heating");
            if (index != -1 && runOutcome.Loads != null)
            {
                dataAccess.SetData(index, runOutcome.Loads.TotalHeating);
            }

            index = Params.IndexOfOutputParam("cooling");
            if (index != -1 && runOutcome.Loads != null)
            {
                dataAccess.SetData(index, runOutcome.Loads.TotalCooling);
            }

            index = Params.IndexOfOutputParam("diagnostics");
            if (index != -1)
            {
                dataAccess.SetDataList(index, runOutcome.Diagnostics.ConvertAll(x => x.ToString()));
            }
        }

        private sealed class RunOutcome
        {
            internal Core.OpenStudio.OpenStudioRunResult RunResult { get; }
            internal Analytical.OpenStudio.OpenStudioLoadSummary Loads { get; }
            internal List<Core.OpenStudio.OpenStudioDiagnostic> Diagnostics { get; }

            internal RunOutcome(Core.OpenStudio.OpenStudioRunResult runResult, Analytical.OpenStudio.OpenStudioLoadSummary loads, List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics)
            {
                RunResult = runResult;
                Loads = loads;
                Diagnostics = diagnostics;
            }
        }
    }
}
