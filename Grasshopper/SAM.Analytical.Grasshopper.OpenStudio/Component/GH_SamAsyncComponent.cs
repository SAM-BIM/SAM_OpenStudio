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

            if (completed.IsCanceled)
            {
                // Task.Run observes the token before the body runs, so a cancel that lands in
                // that window completes the task as Canceled rather than producing a result.
                // Harvesting it would throw on Task<T>.Result — report and leave outputs clear.
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The simulation was cancelled");
                return;
            }

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

        /// <summary>
        /// Deleting the component mid-run must not leave openstudio.exe / energyplus.exe
        /// executing to completion (review P2-05): the token is cancelled and the runner kills
        /// the whole process tree — the same path as the cancel_ input and the timeout.
        /// </summary>
        public override void RemovedFromDocument(GH_Document document)
        {
            CancelRunningTask();
            base.RemovedFromDocument(document);
        }

        /// <summary>Closing the document cancels a running simulation (review P2-05); other context changes (lock, unload, document switch) leave the run alive.</summary>
        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close)
            {
                CancelRunningTask();
            }

            base.DocumentContextChanged(document, context);
        }

        private void CancelRunningTask()
        {
            if (task == null || task.IsCompleted)
            {
                return;
            }

            try
            {
                cancellationTokenSource?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // the source was disposed by a newer solve — that run is no longer ours to stop
            }
        }

        private void ScheduleCompletion(Task runningTask)
        {
            GH_Document document = OnPingDocument();
            if (document == null)
            {
                return;
            }

            runningTask.ContinueWith(completedTask =>
            {
                // Review P2-05: by completion time the component may have been deleted or the
                // document closed — re-resolve the document and never schedule into a dead one.
                GH_Document currentDocument = OnPingDocument();
                if (currentDocument == null)
                {
                    return;
                }

                try
                {
                    currentDocument.ScheduleSolution(10, scheduleDocument => ExpireSolution(false));
                }
                catch (Exception)
                {
                    // the document is disposing/closed — the completed run is dropped by design
                }
            });
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
