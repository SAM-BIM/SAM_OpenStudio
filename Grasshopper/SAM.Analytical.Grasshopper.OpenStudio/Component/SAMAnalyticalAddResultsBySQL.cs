// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Core.Grasshopper;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SAM.Analytical.Grasshopper.OpenStudio
{
    public class SAMAnalyticalAddResultsBySQL : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("c6c53281-ccab-4872-8278-ee8c638be035");

        /// <summary>
        /// The latest version of this component
        /// </summary>
        public override string LatestComponentVersion => "1.0.6";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.SAM_OpenStudio;

        /// <summary>
        /// Initializes a new instance of the SAMGeometryByGHGeometry class.
        /// </summary>
        public SAMAnalyticalAddResultsBySQL()
          : base("SAMAnalytical.AddResultsBySQL", "SAMAnalytical.AddResultsBySQL",
              "Adds Results From OpenStudio Sql Database",
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
            MenuHelper.GoToFileDirectory(MenuHelper.GetVolatileString(this, "_sQLPath"));
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new GooAnalyticalObjectParam() { Name = "_analytical", NickName = "_analytical", Description = "SAM Analytical Object such as AdjacencyCluster or AnalyticalModel", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "_sQLPath", NickName = "_sQLPath", Description = "SQL File Path", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Boolean param_Boolean = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_run", NickName = "_run", Description = "Run", Access = GH_ParamAccess.item };
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
                result.Add(new GH_SAMParam(new GooAnalyticalObjectParam() { Name = "analytical", NickName = "analytical", Description = "SAM Analytical Object such as AdjacencyCluster or AnalyticalModel", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooResultParam() { Name = "spaceSimulationResults", NickName = "spaceSimulationResults", Description = "SAM Analytical SpaceSimulationResults", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooResultParam() { Name = "panelSimulationResults", NickName = "panelSimulationResults", Description = "SAM Analytical panel simulation results (the SAM class is SurfaceSimulationResult; the output name is kept for compatibility)", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "successful", NickName = "successful", Description = "Correctly saved?", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                return result.ToArray();
            }
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="dataAccess">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            int index = Params.IndexOfOutputParam("successful");
            if (index != -1)
            {
                dataAccess.SetData(index, false);
            }

            bool run = false;
            index = Params.IndexOfInputParam("_run");
            if (index == -1 || !dataAccess.GetData(index, ref run) || !run)
                return;

            IAnalyticalObject analyticalObject = null;
            index = Params.IndexOfInputParam("_analytical");
            if (index == -1 || !dataAccess.GetData(index, ref analyticalObject) || analyticalObject == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            string path = null;
            index = Params.IndexOfInputParam("_sQLPath");
            if (index != -1)
            {
                dataAccess.GetData(index, ref path);
            }

            List<Core.Result> results = null;
            List<string> diagnostics = null;

            if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
            {
                if (analyticalObject is AdjacencyCluster)
                {
                    AdjacencyCluster adjacencyCluster = new AdjacencyCluster((AdjacencyCluster)analyticalObject);
                    results = Analytical.OpenStudio.Modify.AddResults(adjacencyCluster, path, out diagnostics);
                    analyticalObject = adjacencyCluster;
                }
                else if (analyticalObject is AnalyticalModel)
                {
                    // Non-mutating convention: the source model's cluster is cloned before any
                    // result is attached (previously the original cluster was modified in place).
                    AdjacencyCluster adjacencyCluster = new AdjacencyCluster(((AnalyticalModel)analyticalObject).AdjacencyCluster);
                    results = Analytical.OpenStudio.Modify.AddResults(adjacencyCluster, path, out diagnostics);
                    analyticalObject = new AnalyticalModel((AnalyticalModel)analyticalObject, adjacencyCluster);

                }
                else if (analyticalObject is BuildingModel)
                {
                    BuildingModel buildingModel = new BuildingModel((BuildingModel)analyticalObject);
                    results = Analytical.OpenStudio.Modify.AddResults(buildingModel, path);
                    analyticalObject = buildingModel;
                }
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Format("SQL file not found: {0}", path));
            }

            if (diagnostics != null)
            {
                foreach (string diagnostic in diagnostics)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, diagnostic);
                }
            }

            index = Params.IndexOfOutputParam("analytical");
            if (index != -1)
            {
                dataAccess.SetData(index, analyticalObject);
            }

            index = Params.IndexOfOutputParam("spaceSimulationResults");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results?.FindAll(x => x is SpaceSimulationResult).ConvertAll(x => new GooResult(x)));
            }

            index = Params.IndexOfOutputParam("panelSimulationResults");
            if (index != -1)
            {
                dataAccess.SetDataList(index, results?.FindAll(x => x is SurfaceSimulationResult).ConvertAll(x => new GooResult(x)));
            }

            index = Params.IndexOfOutputParam("successful");
            if (index != -1)
            {
                dataAccess.SetData(index, results != null && results.Count != 0);
            }
        }
    }
}
