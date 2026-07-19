// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SAM.Analytical.OpenStudio
{
    /// <summary>
    /// Best-effort Windows Job Object wrapper used to terminate a process tree on timeout:
    /// every child the CLI spawns (EnergyPlus) belongs to the job and is closed with it
    /// (JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE). All operations degrade silently — when job
    /// creation or assignment fails the caller falls back to killing the root process only.
    /// </summary>
    internal static class ProcessJobObject
    {
        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public IntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInformationClass, IntPtr lpJobObjectInformation, uint cbJobObjectInformationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        /// <summary>Creates a kill-on-close job object, or <see cref="IntPtr.Zero"/> on failure.</summary>
        public static IntPtr CreateKillOnCloseJob()
        {
            try
            {
                IntPtr jobHandle = CreateJobObject(IntPtr.Zero, null);
                if (jobHandle == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }

                JOBOBJECT_EXTENDED_LIMIT_INFORMATION information = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                {
                    BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                    {
                        LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
                    },
                };

                int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                IntPtr pointer = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.StructureToPtr(information, pointer, false);
                    if (!SetInformationJobObject(jobHandle, JobObjectExtendedLimitInformation, pointer, (uint)length))
                    {
                        CloseHandle(jobHandle);
                        return IntPtr.Zero;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pointer);
                }

                return jobHandle;
            }
            catch (Exception)
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>Assigns a process to the job (best effort; returns false on failure).</summary>
        public static bool TryAssign(IntPtr jobHandle, Process process)
        {
            if (jobHandle == IntPtr.Zero || process == null)
            {
                return false;
            }

            try
            {
                bool result = AssignProcessToJobObject(jobHandle, process.Handle);
                if (!result)
                {
                    System.Diagnostics.Debug.WriteLine("ProcessJobObject: AssignProcessToJobObject failed, error " + Marshal.GetLastWin32Error());
                }

                return result;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine("ProcessJobObject: assignment threw " + exception.GetType().Name);
                return false;
            }
        }

        /// <summary>Terminates every process currently assigned to the job (best effort).</summary>
        public static void Terminate(IntPtr jobHandle)
        {
            if (jobHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                TerminateJobObject(jobHandle, 1);
            }
            catch (Exception)
            {
                // best effort only
            }
        }

        /// <summary>Closes the job handle, terminating any processes still assigned to it.</summary>
        public static void Close(IntPtr jobHandle)
        {
            if (jobHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                CloseHandle(jobHandle);
            }
            catch (Exception)
            {
                // best effort only
            }
        }
    }
}
