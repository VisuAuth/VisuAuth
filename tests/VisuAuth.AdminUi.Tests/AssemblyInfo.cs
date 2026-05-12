using Xunit;

// All test classes here share the sample app's SQLite database. xUnit's default
// is to run classes in parallel, which causes lock contention against the file.
// Serialise the entire assembly until we move the sample to an in-memory store.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
