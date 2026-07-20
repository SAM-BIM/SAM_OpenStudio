// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Core.Grasshopper;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SAM.Analytical.Grasshopper.OpenStudio
{
    /// <summary>
    /// Local task-capable base for SAM OpenStudio components (C6 — no task-capable base exists
    /// anywhere in the SAM ecosystem, and SAM core's GH_SAMComponent must not change). Pattern:
    /// inputs are read and signed on the UI thread; the work runs on a background task with a
    /// CancellationToken; completion reschedules the document so results are harvested on the
    /// UI thread. Changed inputs cancel the stale run (never duplicate runs), a cancel_ input
    /// cancels explicitly, outputs are cleared when a run starts (no stale results), and no
    /// native OpenStudio objects ever cross the component boundary — only paths and numbers are
    /// harvested.
    /// </summary>
    public abstract class GH_SamAsyncComponent : GH_SAMVariableOutputParameterComponent
    {
        private Task task;
        private CancellationTokenSource cancellationTokenSource;
        private string signature;

        protected GH_SamAsyncComponent(string name, string nickname, string description, string category, string subCategory)
            : base(name, nickname, description, category, subCategory)
        {
        }

        protected override sealed void SolveInstance(IGH_DataAccess dataAccess)
        {
            bool cancel = false;
            int cancelIndex = Params.IndexOfInputParam("cancel_");
            if (cancelIndex != -1)
            {
                dataAccess.GetData(cancelIndex, ref cancel);
            }

            if (cancel && task != null && !task.IsCompleted)
            {
                cancellationTokenSource?.Cancel();
            }

            string currentSignature = ComputeSignature(dataAccess);
            if (task == null || !string.Equals(currentSignature, signature, StringComparison.Ordinal))
            {
                if (task != null && !task.IsCompleted)
                {
                    // Inputs changed mid-run: cancel the stale run — never duplicate runs.
                    cancellationTokenSource?.Cancel();
                }

                signature = currentSignature;
                if (currentSignature == null)
                {
                    // Invalid inputs (the component reported its own error) — nothing running.
                    task = null;
                    Message = "Ready";
                    return;
                }

                cancellationTokenSource?.Dispose();
                cancellationTokenSource = new CancellationTokenSource();
                task = CreateTask(dataAccess, cancellationTokenSource.Token);
                if (task == null)
                {
                    signature = null;
                    Message = "Ready";
                    return;
                }

                Message = "Running";
                ClearOutputs(dataAccess);
                ScheduleCompletion(task);
                return;
            }

            if (!task.IsCompleted)
            {
                return; // still running; outputs stay cleared until harvest
            }

            Task completed = task;
            task = null;
            Message = completed.IsCanceled ? "Cancelled" : completed.IsFaulted ? "Failed" : "Completed";
            Harvest(completed, dataAccess);
        }

        /// <summary>Registers the shared optional cancel_ input; components append it last.</summary>
        protected static GH_SAMParam CreateCancelParam()
        {
            global::Grasshopper.Kernel.Parameters.Param_Boolean param_Boolean = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "cancel_", NickName = "cancel_", Description = "True cancels the running simulation", Access = GH_ParamAccess.item, Optional = true };
            param_Boolean.SetPersistentData(false);
            return new GH_SAMParam(param_Boolean, ParamVisibility.Voluntary);
        }

        /// <summary>Deterministic signature of the current inputs; null when inputs are invalid (component must report why).</summary>
        protected abstract string ComputeSignature(IGH_DataAccess dataAccess);

        /// <summary>Starts the background work. Inputs must be re-read inside (called once per signature).</summary>
        protected abstract Task CreateTask(IGH_DataAccess dataAccess, CancellationToken cancellationToken);

        /// <summary>Harvests a completed task onto the outputs (UI thread).</summary>
        protected abstract void Harvest(Task task, IGH_DataAccess dataAccess);

        private void ScheduleCompletion(Task runningTask)
        {
            GH_Document document = OnPingDocument();
            if (document == null)
            {
                return;
            }

            runningTask.ContinueWith(completedTask => document.ScheduleSolution(10, scheduleDocument => ExpireSolution(false)));
        }

        private void ClearOutputs(IGH_DataAccess dataAccess)
        {
            for (int i = 0; i < Params.Output.Count; i++)
            {
                try
                {
                    dataAccess.SetData(i, null);
                }
                catch (Exception)
                {
                    // clearing is best effort; GH drops unset outputs anyway
                }
            }
        }
    }
}
