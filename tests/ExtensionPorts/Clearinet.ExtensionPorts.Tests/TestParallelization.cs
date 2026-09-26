using Xunit;

// Every test class here swaps the compatibility layer's static host hooks
// (CompatShimHost.Log, CompatShimHost.Preferences, ...) and loads real
// extensions that use them. xUnit runs different test classes in parallel
// by default, so one class's hooks could replace another's mid-test (the
// CSP tests' log went to the NetLog tests' hook). Run them one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
