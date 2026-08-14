using Xunit;

// 测试类间并行会与 AsyncPostContext（自定义 SynchronizationContext 串行化 Post）互相干扰
// （高并发回归测试在并行负载下偶发线程池饥饿卡死）——串行执行保持稳定。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
