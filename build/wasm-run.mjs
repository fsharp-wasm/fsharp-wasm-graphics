// node build/wasm-run.mjs <module.wasm> <run-export> <checks-export> [<first-failure-export>]
// Runs a test module that was built from a plain-checks project (tests/*ChartsTests and the like): it calls the exported `run`, which returns the number of failures, and
// `checks`, which says how many checks the run made (a module whose tests were compiled to nothing must not pass).  The host side of strings and closures is the one of
// fsharp-wasm (tests/HostInterop/host.mjs, found through FSHARP_WASM_DIR).  The module prints the name of a failed check with `printfn`, which hands the host a string of the
// module's own (a GC array): the host cannot read it, so a failure is a number here, and the name is what the .NET run of the same project prints.
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

const [file, runName, checksName, firstName] = process.argv.slice(2);
if (!file || !runName || !checksName) { console.error("usage: wasm-run.mjs <module.wasm> <run-export> <checks-export>"); process.exit(2); }
const dir = process.env.FSHARP_WASM_DIR;
if (!dir) { console.error("FSHARP_WASM_DIR must name a checkout of fsharp-wasm (tests/HostInterop/host.mjs is the host of the compiled modules)"); process.exit(2); }
const { hostImports, compileOptions } = await import(pathToFileURL(path.join(dir, "tests/HostInterop/host.mjs")).href);

let instance;
({ instance } = await WebAssembly.instantiate(fs.readFileSync(file), { ...hostImports(() => instance), env: { consolePrint: () => {} } }, compileOptions));
const failures = instance.exports[runName]();
const checks = instance.exports[checksName]();
const first = firstName && failures > 0 ? `, the first is check number ${instance.exports[firstName]()}` : "";
console.log(`${path.basename(path.dirname(file))}: ${checks} checks, ${failures} failed${first}`);
process.exit(failures === 0 && checks > 0 ? 0 : 1);
