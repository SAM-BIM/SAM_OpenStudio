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
    public class OpenStudioToSAMAnalytical : GH_SAMAsyncComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("3c8b41d7-92e5-4a06-b1f4-6d2a7c9e08b3");

        /// <summary>
        /// The latest version of this component
        /// </summary>
        public override string LatestComponentVersion => "1.0.0";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.SAM_OpenStudio;

        /// <summary>
        /// Imports an OpenStudio model (OSM) or workflow (OSW) into a SAM AnalyticalModel.
        /// Non-blocking: the import runs on a background task with cancellation, mirroring
        /// OpenStudio.RunModel, because an executed OSW workflow can take minutes. A direct OSM
        /// takes the same path so the component contract does not change with the input kind.
        /// Thin wrapper over SAM.Analytical.OpenStudio.Convert.ToSAM — no conversion rules live
        /// here.
        /// </summary>
        public OpenStudioToSAMAnalytical()
          : base("OpenStudio.SAMAnalytical", "OpenStudio.SAMAnalytical",
              "Imports an OpenStudio model (OSM) or workflow (OSW) into a SAM AnalyticalModel — asynchronous, cancellable",
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
                // Input order is part of the released contract: a Grasshopper document stores
                // connections by index, so these must never be reordered. Optional inputs are
                // placed after the required path and before the cancel parameter, which the
                // asynchronous base class expects last.
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "_path", NickName = "_path", Description = "OSM or OSW file path", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Boolean param_Boolean = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "executeWorkflow_", NickName = "executeWorkflow_", Description = "OSW input only. False (default) imports the workflow's seed model and warns that its measures were NOT applied. True runs the workflow through the OpenStudio CLI and imports the final post-model-measure model. Ignored for OSM input.", Access = GH_ParamAccess.item, Optional = true };
                param_Boolean.SetPersistentData(false);
                result.Add(new GH_SAMParam(param_Boolean, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "outputDirectory_", NickName = "outputDirectory_", Description = "Directory for OSW workflow execution (an isolated run folder is created inside it). Defaults to the OSW's own directory. Unused for OSM input.", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Binding));

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
                result.Add(new GH_SAMParam(new GooAnalyticalModelParam() { Name = "analyticalModel", NickName = "analyticalModel", Description = "Imported SAM AnalyticalModel", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "resolvedOsmPath", NickName = "resolvedOsmPath", Description = "The OSM actually converted: the input itself for an OSM, the resolved seed for a non-executed OSW, or the final post-model-measure OSM for an executed workflow", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "diagnostics", NickName = "diagnostics", Description = "Import diagnostics: every approximation, unsupported object and failure", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "successful", NickName = "successful", Description = "True when a SAM AnalyticalModel was produced and no diagnostic has Error severity", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                return result.ToArray();
            }
        }

        protected override string ComputeSignature(IGH_DataAccess dataAccess)
        {
            string path = null;
            int index = Params.IndexOfInputParam("_path");
            if (index == -1 || !dataAccess.GetData(index, ref path) || string.IsNullOrWhiteSpace(path))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid _path data");
                return null;
            }

            bool executeWorkflow = false;
            index = Params.IndexOfInputParam("executeWorkflow_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref executeWorkflow);
            }

            string outputDirectory = null;
            index = Params.IndexOfInputParam("outputDirectory_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref outputDirectory);
            }

            return string.Format("{0}|{1}|{2}", path, executeWorkflow, outputDirectory);
        }

        protected override Task CreateTask(IGH_DataAccess dataAccess, CancellationToken cancellationToken)
        {
            string path = null;
            dataAccess.GetData(Params.IndexOfInputParam("_path"), ref path);

            bool executeWorkflow = false;
            dataAccess.GetData(Params.IndexOfInputParam("executeWorkflow_"), ref executeWorkflow);

            string outputDirectory = null;
            dataAccess.GetData(Params.IndexOfInputParam("outputDirectory_"), ref outputDirectory);

            Core.OpenStudio.OpenStudioImportOptions openStudioImportOptions = new Core.OpenStudio.OpenStudioImportOptions
            {
                ExecuteWorkflow = executeWorkflow,
                OutputDirectory = string.IsNullOrWhiteSpace(outputDirectory) ? null : outputDirectory,
            };

            return Analytical.OpenStudio.Convert.ToSAMAsync(path, openStudioImportOptions, null, null, cancellationToken);
        }

        protected override void Harvest(Task task, IGH_DataAccess dataAccess)
        {
            if (task.IsFaulted)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, task.Exception?.GetBaseException().Message ?? "Import failed");
                return;
            }

            Analytical.OpenStudio.OpenStudioImportResult openStudioImportResult = ((Task<Analytical.OpenStudio.OpenStudioImportResult>)task).Result;
            if (openStudioImportResult == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Import produced no result");
                return;
            }

            int index = Params.IndexOfOutputParam("analyticalModel");
            if (index != -1 && openStudioImportResult.AnalyticalModel != null)
            {
                dataAccess.SetData(index, new GooAnalyticalModel(openStudioImportResult.AnalyticalModel));
            }

            index = Params.IndexOfOutputParam("resolvedOsmPath");
            if (index != -1)
            {
                dataAccess.SetData(index, openStudioImportResult.ResolvedOsmPath);
            }

            index = Params.IndexOfOutputParam("diagnostics");
            if (index != -1)
            {
                List<string> diagnostics = new List<string>();
                foreach (Core.OpenStudio.OpenStudioDiagnostic openStudioDiagnostic in openStudioImportResult.Diagnostics)
                {
                    diagnostics.Add(openStudioDiagnostic.ToString());
                }

                dataAccess.SetDataList(index, diagnostics);
            }

            index = Params.IndexOfOutputParam("successful");
            if (index != -1)
            {
                dataAccess.SetData(index, openStudioImportResult.IsValid);
            }

            // Surface the outcome on the component itself: a Grasshopper user reads the canvas
            // long before they read a diagnostics panel, and an import that silently produced an
            // errored model would otherwise look like it worked.
            foreach (Core.OpenStudio.OpenStudioDiagnostic openStudioDiagnostic in openStudioImportResult.Diagnostics)
            {
                if (openStudioDiagnostic == null)
                {
                    continue;
                }

                if (openStudioDiagnostic.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, openStudioDiagnostic.ToString());
                }
                else if (openStudioDiagnostic.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, openStudioDiagnostic.ToString());
                }
            }
        }
    }
}
