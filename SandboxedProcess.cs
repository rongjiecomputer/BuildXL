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

        /// <nodoc/>
        public SandboxedProcess(PathTable pathTable)
        {
            m_loggingContext = new LoggingContext(nameof(SandboxedProcess));
            m_pathTable = pathTable;
        }

        /// <nodoc />
        public Task<SandboxedProcessResult> Run(SandboxOptions option)
        {
            var pathToProcess = option.args[0];
            var arguments = String.Join(" ", option.args.Skip(1));

            var info = new SandboxedProcessInfo(
                m_pathTable,
                this,
                pathToProcess,
                CreateManifest(AbsolutePath.Create(m_pathTable, pathToProcess), option.bind_mount_sources),
                disableConHostSharing: true,
                containerConfiguration: ContainerConfiguration.DisabledIsolation,
                loggingContext: m_loggingContext)
            {
                Arguments = arguments,
                WorkingDirectory = option.working_dir.ToString(m_pathTable),
                PipSemiStableHash = 0,
                PipDescription = "BazelSandboxedProcess",
                SandboxedKextConnection = OperatingSystemHelper.IsUnixOS ? new KextConnection() : null
            };

            var process = SandboxedProcessFactory.StartAsync(info, forceSandboxing: true).GetAwaiter().GetResult();

            return process.GetResultAsync();
        }

        /// <nodoc />
        string ISandboxedProcessFileStorage.GetFileName(SandboxedProcessFile file)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), file.DefaultFileName());
        }

        /// <nodoc />
        private FileAccessManifest CreateManifest(AbsolutePath pathToProcess, IEnumerable<AbsolutePath> directoriesToBlock)
        {
            var fileAccessManifest = new FileAccessManifest(m_pathTable)
            {
                FailUnexpectedFileAccesses = true,
                ReportFileAccesses = true,
                MonitorChildProcesses = true,
            };

            // We allow all file accesses at the root level, so by default everything is allowed
            fileAccessManifest.AddScope(AbsolutePath.Invalid, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowAll);

            // We explicitly allow reading from the tool path
            fileAccessManifest.AddPath(pathToProcess, FileAccessPolicy.MaskAll, FileAccessPolicy.AllowRead);

            /*// We block access on all provided directories
            foreach (var directoryToBlock in directoriesToBlock)
            {
                fileAccessManifest.AddScope(
                    directoryToBlock,
                    FileAccessPolicy.MaskAll,
                    FileAccessPolicy.Deny & FileAccessPolicy.ReportAccess);
            }*/

            return fileAccessManifest;
        }
    }
}
