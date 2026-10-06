// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Core.Grasshopper;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SAM.Analytical.Grasshopper.OpenStudio
{
    public class OpenStudioCreateDesignDaysBySQL : GH_SAMVariableOutputParameterComponent
    {
        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("f268aec3-ffd8-4055-b623-5aafc1808416");

        /// <summary>
        /// The latest version of this component
        /// </summary>
        public override string LatestComponentVersion => "1.0.3";

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.Resources.SAM_OpenStudio;

        /// <summary>
        /// Initializes a new instance of the SAMGeometryByGHGeometry class.
        /// </summary>
        public OpenStudioCreateDesignDaysBySQL()
          : base("OpenStudio.CreateDesignDaysBySQL", "OpenStudio.CreateDesignDaysBySQL",
              "Query DesignDays From OpenStudio Sql Database",
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
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "_sQLPath", NickName = "_sQLPath", Description = "SQL File Path", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
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
                result.Add(new GH_SAMParam(new GooAnalyticalObjectParam() { Name = "designDays", NickName = "designDays", Description = "SAM Analytical DesignDays", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                return result.ToArray();
            }
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="dataAccess">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            string path = null;
            int index = Params.IndexOfInputParam("_sQLPath");
            if (index == -1 || !dataAccess.GetData(index, ref path) || path == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            List<DesignDay> result = null;
            List<string> diagnostics = null;
            try
            {
                result = Analytical.OpenStudio.Create.DesignDays(path, out diagnostics);
            }
            catch (Exception exception)
            {
                // Malformed SQL content is a structured diagnostic, never a solution exception.
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Format("Design days could not be read: {0}", exception.Message));
            }

            if (diagnostics != null)
            {
                foreach (string diagnostic in diagnostics)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, diagnostic);
                }
            }

            index = Params.IndexOfOutputParam("designDays");
            if (index != -1)
            {
                dataAccess.SetDataList(index, result);
            }
        }
    }
}
