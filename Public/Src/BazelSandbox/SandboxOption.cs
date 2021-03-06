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
using BuildXL.Processes;
using BuildXL.Utilities;

namespace Bazel {
    /// <summary>
    /// Class to store sandbox configuration
    /// </summary>
    public class SandboxOptions {
        /// <nodoc />
        public static readonly uint kInfiniteTime = 0;

        /// <summary>
        /// Working directory (-W)
        /// </summary>
        public AbsolutePath working_dir { get; private set; } = AbsolutePath.Invalid;

        /// <summary>
        /// How long to wait before killing the child (-T)
        /// </summary>
        public uint timeout_secs { get; private set; } = kInfiniteTime;
        /// <summary>
        /// How long to wait before sending SIGKILL in case of timeout (-t)
        /// </summary>
        public uint kill_delay_secs { get; private set; } = kInfiniteTime;
        /// <summary>
        /// Where to redirect stdout (-l)
        /// </summary>
        public AbsolutePath stdout_path { get; private set; } = AbsolutePath.Invalid;
        /// <summary>
        /// Where to redirect stderr (-L)
        /// </summary> 
        public AbsolutePath stderr_path { get; private set; } = AbsolutePath.Invalid;
        /// <summary>
        /// Files or directories to make readonly for the sandboxed process (-r)
        /// </summary>
        public List<AbsolutePath> readonly_files { get; private set; } = new List<AbsolutePath>();
        /// <summary>
        /// Files or directories to make read/writable for the sandboxed process (-w)
        /// </summary>
        public List<AbsolutePath> writable_files { get; private set; } = new List<AbsolutePath>();
        /// <summary>
        /// Files or directories to make inaccessible for the sandboxed process (-w)
        /// </summary>
        public List<AbsolutePath> blocked_files { get; private set; } = new List<AbsolutePath>();
        // Where to write stats, in protobuf format (-S)
        // public AbsolutePath stats_path { get; private set; } = AbsolutePath.Invalid;
        /// <summary>
        /// Print debugging messages (-D)
        /// </summary>
        public bool debug = false;
        /// <summary>
        /// Command to run (--)
        /// </summary>
        public List<string> args { get; private set; } = null;

        /// <nodoc />
        public SandboxOptions() { }

        /// <summary>
        /// Parse args into SandboxOptions
        /// </summary>
        public void ParseOptions(List<string> args, PathTable pathTable)
        {
            args = ExpandArguments(args);
            int i = 0;
            for (; i < args.Count() && args[i] != "--"; i++)
            {
                var arg = args[i];
                if (arg.Count() > 1 && (arg[0] == '/' || arg[0] == '-'))
                {
                    var name = arg.Substring(1);
                    switch (name)
                    {
                        case "h":
                            {
                                PrintUsage(0);
                                break;
                            }
                        case "W":
                            {
                                var path = AbsolutePath.Invalid;
                                if (!AbsolutePath.TryCreate(pathTable, args[++i], out path))
                                {
                                    ExitWithError($"Cannot create absolute path from '{args[i]}'");
                                }
                                this.working_dir = path;
                                break;
                            }
                        case "T":
                            {
                                try
                                {
                                    this.timeout_secs = Convert.ToUInt32(args[++i]);
                                }
                                catch (Exception e)
                                {
                                    ExitWithError($"{args[i]} is not valid number:\n{e.ToString()}");

                                }
                                break;
                            }
                        case "t":
                            {
                                try
                                {
                                    this.kill_delay_secs = Convert.ToUInt32(args[++i]);
                                }
                                catch (Exception e)
                                {
                                    ExitWithError($"{args[i]} is not valid number:\n{e.ToString()}");
                                }
                                break;
                            }
                        case "l":
                            {
                                var path = AbsolutePath.Invalid;
                                if (!AbsolutePath.TryCreate(pathTable, args[++i], out path))
                                {
                                    ExitWithError($"Cannot create absolute path from '{args[i]}'");
                                }
                                this.stdout_path = path;
                                break;
                            }
                        case "L":
                            {
                                var path = AbsolutePath.Invalid;
                                if (!AbsolutePath.TryCreate(pathTable, args[++i], out path))
                                {
                                    ExitWithError($"Cannot create absolute path from '{args[i]}'");
                                }
                                this.stderr_path = path;
                                break;
                            }
                        case "w":
                            {
                                var path = AbsolutePath.Invalid;
                                if (!AbsolutePath.TryCreate(pathTable, args[++i], out path))
                                {
                                    ExitWithError($"Cannot create absolute path from '{args[i]}'");
                                }
                                this.writable_files.Add(path);
                                break;
                            }
                        case "r":
                            {
                                var path = AbsolutePath.Invalid;
                                if (!AbsolutePath.TryCreate(pathTable, args[++i], out path))
                                {
                                    ExitWithError($"Cannot create absolute path from '{args[i]}'");
                                }
                                this.readonly_files.Add(path);
                                break;
                            }
                        case "b":
                            {
                                var path = AbsolutePath.Invalid;
                                if (!AbsolutePath.TryCreate(pathTable, args[++i], out path))
                                {
                                    ExitWithError($"Cannot create absolute path from '{args[i]}'");
                                }
                                this.blocked_files.Add(path);
                                break;
                            }
                        case "D":
                            {
                                this.debug = true;
                                break;
                            }
                        default:
                            ExitWithError($"Unknown option: {arg}");
                            break;
                    }
                }
                else
                {
                    ExitWithError($"Unknown argument: {arg}");
                }
            }
            if (i >= args.Count - 1 || args[i] != "--")
            {
                ExitWithError("Command to sandboxed not specified");
            }
            this.args = args.Skip(i + 1).ToList();
        }

        /// <summary>
        /// Expand param files in args. Expanding stops when first '--' is encountered.
        /// </summary>
        private static List<string> ExpandArguments(IList<string> args)
        {
            List<string> expanded = new List<string>(args.Count());
            for (int i = 0; i < args.Count(); i++)
            {
                string arg = args[i];
                if (arg.Length > 1 && arg[0] == '@')
                {
                    expanded.AddRange(File.ReadLines(arg.Substring(1)));
                }
                else if (arg == "--")
                {
                    expanded.AddRange(args.Skip(i));
                    break;
                }
                else
                {
                    expanded.Add(arg);
                }
            }
            return expanded;
        }

        private void ExitWithError(string msg) {
            Console.WriteLine(msg);
            PrintUsage(1);
        }

        private void PrintUsage(int exitCode) {
            var processName = Process.GetCurrentProcess().ProcessName;
            Console.Write(
                    $"\nUsage: {processName} [option...] -- command [arg...]\n" +
                    "\nPossible arguments:\n" +
                    "  -W <working-dir>  working directory (uses current directory if " +
                    "not specified)\n" +
                    "  -T <timeout>  timeout after which the child process will be " +
                    "terminated with SIGTERM\n" +
                    "  -t <timeout>  in case timeout occurs, how long to wait before " +
                    "killing the child with SIGKILL\n" +
                    "  -l <file>  redirect stdout to a file\n" +
                    "  -L <file>  redirect stderr to a file\n" +
                    "  -w <file>  make a file or directory read/writable for the sandboxed " +
                    "process\n" +
                    "  -r <file>  make a file or directory readonly for the sandboxed " +
                    "process\n" +
                    "  -b <file>  make a file or directory inaccessible for the sandboxed " +
                    "process\n" +
                    // "  -S <file>  if set, write stats in protobuf format to a file\n" +
                    "  -D  print debug messages to stdout\n" +
                    "  @FILE  read newline-separated arguments from FILE\n" +
                    "  --  command to run inside sandbox, followed by arguments\n");
            Environment.Exit(exitCode);
        }
    }
}
