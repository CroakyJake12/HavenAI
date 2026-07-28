using Xunit;

// Recovery policy tests deliberately toggle the process-wide safe-mode flag.
// Real Haven sets that flag once during startup, so serialize this assembly to
// prevent an artificial test race with tool-availability policy checks.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
