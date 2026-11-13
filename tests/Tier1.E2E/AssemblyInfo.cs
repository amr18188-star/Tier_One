using Xunit;

// The scenarios share one running server and one database and several log in as the same accounts, so they run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
