namespace Launcher.Core.Launch;

/// <summary>
/// 皮肤文件强写（8-16 批次 51：从 HomeViewModel.ForceWriteSkinFile 迁移到 Core，皮肤库/换肤共用）。
/// GC 回收旧 Bitmap 残留锁（旧版本 new Bitmap(path) 的 FileStream 由终结器释放）→ 清只读 → 重试删除
/// → 写 tmp → 原子 Move 覆盖。三次重试仍失败（外部进程独占：游戏运行中/杀毒）→ 抛原始异常。
/// </summary>
public static class SkinFileWriter
{
    public static void ForceWrite(string dest, byte[] bytes)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                if (File.Exists(dest))
                {
                    File.SetAttributes(dest, FileAttributes.Normal);
                    File.Delete(dest);
                }
                var tmp = dest + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                File.Move(tmp, dest, true);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(200); // 短暂占用（杀毒/索引）重试
            }
        }
    }
}
