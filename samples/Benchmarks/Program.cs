using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

IConfig? config = null;
#if DEBUG
config = new DebugInProcessConfig();
#endif
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
