using Xunit;

// Infrastructure recovery tests deliberately change the process-wide safe-mode
// flag. Serialize this assembly so those changes cannot leak into provider and
// document operations running at the same instant.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
