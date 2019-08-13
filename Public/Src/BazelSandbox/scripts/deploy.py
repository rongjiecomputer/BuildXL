# Remove all references to UCRT DLLs in deps.json
import json
import os

DOT_NETCORE_VER = ".NETCoreApp,Version=v3.0"
DOT_NETCORE_RUNTIME = "runtime.win-x64.Microsoft.NETCore.App/3.0.0-preview5-27626-15"

depfile = os.path.join(os.environ.get('TEMP'), 'BazelSandboxDeploy', 'BazelSandbox.deps.json')
with open(depfile, 'r') as f:
  obj = json.load(f)

native = obj["targets"][DOT_NETCORE_VER][DOT_NETCORE_RUNTIME]["native"]
for key in list(native.keys()):
  if key.startswith("runtimes/win-x64/native/api-ms-win") or key == "runtimes/win-x64/native/ucrtbase.dll":
    del native[key]

with open(depfile, 'w') as f:
  json.dump(obj, f)
