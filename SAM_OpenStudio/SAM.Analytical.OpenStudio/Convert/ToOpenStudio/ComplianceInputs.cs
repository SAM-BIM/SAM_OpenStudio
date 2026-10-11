// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        // The Ideal Loads translator consumes materialised geometry, internal conditions and
        // schedules. It does not execute Part F airflow or Part O preparation/scenario workflows.
        // Report design inputs explicitly instead of silently treating an export as their execution.
        private static void ReportComplianceInputs(OpenStudioConversionContext context)
        {
            ReportComplianceInput(context, context.Source, AnalyticalModelParameter.OverheatingScenarios);
            ReportComplianceInput(context, context.Source, AnalyticalModelParameter.PartOEquipmentSelection);
            ReportComplianceInput(context, context.Source, AnalyticalModelParameter.PartOProjectTestVentilationUnit);
            ReportComplianceInput(context, context.Source, AnalyticalModelParameter.PartODwellingStrategies);
            ReportComplianceInput(context, context.Source, AnalyticalModelParameter.PartOManualEquipmentSelection);

            AdjacencyCluster cluster = context.Source.AdjacencyCluster;
            if (cluster == null)
            {
                return;
            }

            foreach (Space space in cluster.GetSpaces() ?? new System.Collections.Generic.List<Space>())
            {
                ReportComplianceInput(context, space, SpaceParameter.PartFLocalExtractMethod);
            }

            foreach (Panel panel in cluster.GetPanels() ?? new System.Collections.Generic.List<Panel>())
            {
                foreach (Aperture aperture in panel.Apertures ?? new System.Collections.Generic.List<Aperture>())
                {
                    ReportComplianceInput(context, aperture, ApertureParameter.PartFDoorTransferData);
                }
            }
        }

        private static void ReportComplianceInput(OpenStudioConversionContext context, Core.SAMObject source, Enum parameter)
        {
            if (source == null || !source.TryGetValue(parameter, out object value) || value == null)
            {
                return;
            }

            context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ComplianceInputUnsupported,
                Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning,
                string.Format("{0}.{1} is present but is not applied by the Ideal Loads translator. Part F airflow and Part O scenario, strategy and equipment preparation are outside this conversion; supply the intended materialised geometry, internal conditions and schedules before export.", parameter.GetType().Name, parameter), source);
        }
    }
}
