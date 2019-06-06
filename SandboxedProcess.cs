// Copyright 2019 The Bazel Authors. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bazel;
using BuildXL.Processes;
using BuildXL.Processes.Containers;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace Bazel
{
    /// <summary>
    /// A program is runned in the sandbox, where only file access to some directories/files are allowed
    /// </summary>
    public class SandboxedProcess : ISandboxedProcessFileStorage
    {
        private readonly LoggingContext m_loggingContext;
        private readonly PathTable m_pathTable;
        private StreamWriter m_stdoutF = null;
        private StreamWriter m_stderrF = null;

        /// <nodoc/>
        public SandboxedProcess(PathTable pathTable)
        {
            m_loggingContext = new LoggingContext(nameof(SandboxedProcess));
            m_pathTable = pathTable;
            m_stdoutF = null;
            m_stderrF = null;
        }

        /// <nodoc />
        public Task<SandboxedProcessResult> Run(SandboxOptions option)
        {
            var pathToProcess = option.args[0];
            // TODO(rongjiecomputer) Implement real command line creation algorithm
            var arguments = String.Join(" ", option.args.Skip(1));

            var fam = CreateManifest(AbsolutePath.Create(m_pathTable, pathToProcess), option);

            Action<string> stdoutCallback;
            if (option.stdout_path != AbsolutePath.Invalid)
            {
                m_stdoutF = new StreamWriter(option.stdout_path.ToString(m_pathTable));
                stdoutCallback = s => m_stdoutF.WriteLine(s);
            }
            else
            {
                stdoutCallback = s => Console.WriteLine(s);
            }

            Action<string> stderrCallback;
            if (option.stderr_path != AbsolutePath.Invalid)
            {
                m_stderrF = new StreamWriter(option.stderr_path.ToString(m_pathTable));
                stderrCallback = s => m_stderrF.WriteLine(s);
            }
            else
            {
                stderrCallback = s => Console.Error.WriteLine(s);
            }

            var workingDir = option.working_dir != AbsolutePath.Invalid ? option.working_dir.ToString(m_pathTable) : Environment.CurrentDirectory;

            var info = new SandboxedProcessInfo(
                m_pathTable,
                this,
                pathToProcess,
                fam,
                disableConHostSharing: true,
                containerConfiguration: ContainerConfiguration.DisabledIsolation,
                loggingContext: m_loggingContext)
            {
                Arguments = arguments,
                WorkingDirectory = workingDir,
                // PipSemiStableHash = 0,
                PipDescription = "BazelSandboxedProcess",

                StandardOutputEncoding = Encoding.UTF8,
                StandardOutputObserver = stdoutCallback,

                StandardErrorEncoding = Encoding.UTF8,
                StandardErrorObserver = stderrCallback,

                SandboxedKextConnection = OperatingSystemHelper.IsUnixOS ? new KextConnection() : null
            };

            if (option.timeout_secs != SandboxOptions.kInfiniteTime)
            {
                info.Timeout = TimeSpan.FromSeconds(option.timeout_secs);
            }

            if (option.kill_delay_secs != SandboxOptions.kInfiniteTime)
            {
                info.NestedProcessTerminationTimeout = TimeSpan.FromSeconds(option.kill_delay_secs);
            }

            var process = SandboxedProcessFactory.StartAsync(info, forceSandboxing: true).GetAwaiter().GetResult();

            return process.GetResultAsync();
        }

        /// <summary>
        /// Run clean up after sandboxed process ends.
        /// </summary>
        public void Cleanup()
        {
            m_stdoutF?.Close();
            m_stderrF?.Close();
        }

        /// <nodoc />
        string ISandboxedProcessFileStorage.GetFileName(SandboxedProcessFile file)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), file.DefaultFileName());
        }

        /// <nodoc />
        private FileAccessManifest CreateManifest(AbsolutePath pathToProcess, SandboxOptions option)
        {
            var fileAccessManifest = new FileAccessManifest(m_pathTable)
            {
                FailUnexpectedFileAccesses = true,
                ReportFileAccesses = false,
                ReportUnexpectedFileAccesses = false,
                MonitorChildProcesses = true,
            };

            // We block all file accesses at the root level, so by default everything is blocked
            fileAccessManifest.AddScope(AbsolutePath.Invalid, FileAccessPolicy.MaskNothing, FileAccessPolicy.Deny);

            // We explicitly allow reading from the tool path
            fileAccessManifest.AddPath(pathToProcess, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowRead);

            // We allow some special folders and temp folder
            fileAccessManifest.AddScope(
                AbsolutePath.Create(m_pathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.Windows)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAll);

            fileAccessManifest.AddScope(
                AbsolutePath.Create(m_pathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.InternetCache)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAll);

            fileAccessManifest.AddScope(
                AbsolutePath.Create(m_pathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.History)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAll);

            fileAccessManifest.AddScope(
                AbsolutePath.Create(m_pathTable, Environment.GetEnvironmentVariable("TEMP")),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAll);

            // We allow access on all provided files/directories
            // Note: if C:\A is allowed, its subtree is allowed too.
            foreach (var path in option.readonly_files)
            {
                fileAccessManifest.AddScope(
                    path,
                    FileAccessPolicy.MaskAll,
                    FileAccessPolicy.AllowRead);
            }

            foreach (var path in option.writable_files)
            {
                fileAccessManifest.AddScope(
                    path,
                    FileAccessPolicy.MaskAll,
                    FileAccessPolicy.AllowAll);
            }

            return fileAccessManifest;
        }
    }
}
