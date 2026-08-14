using Launcher.Core.Launch;

namespace Launcher.Core.Tests;

/// <summary>AL16：自动内存分配——按可用内存留余量，封顶总内存 60%</summary>
public class MemoryAllocatorTests
{
    [Fact]
    public void Compute_PlentyAvail_CapsAt60PercentOfTotal()
    {
        // 32G 总、可用 20G → min(20G-1.5G, 32G*0.6=19.2G) = 18.5G
        Assert.Equal(18944, MemoryAllocator.Compute(20480, 32768));
    }

    [Fact]
    public void Compute_BusyAvail_LeavesReserve()
    {
        // 16G 总、可用 4G → min(4G-1.5G=2.5G, 9.6G) = 2.5G（内存紧张自动降配）
        Assert.Equal(2560, MemoryAllocator.Compute(4096, 16384));
    }

    [Fact]
    public void Compute_TinyAvail_FloorAt1024()
    {
        // 可用 2G → 2G-1.5G=512 → 下限 1024
        Assert.Equal(1024, MemoryAllocator.Compute(2048, 8192));
    }
}
