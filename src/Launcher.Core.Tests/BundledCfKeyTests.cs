using Launcher.Core.Services;

namespace Launcher.Core.Tests;

/// <summary>
/// 内置 CF key 保护测试（8-19 AES-HMAC 升级）：
/// 加密→解密往返、篡改检测、空值降级。
/// </summary>
public class BundledCfKeyTests
{
    [Fact]
    public void Encode_Decode_RoundTrip()
    {
        // 占位假值——往返只验证加解密互逆，绝不使用真实 key（真实 key 只经环境变量注入）
        var plain = "test-cf-key-placeholder-0123456789";
        var enc = BundledCfKey.EncodeForBundling(plain);
        Assert.NotNull(enc);
        Assert.StartsWith("AES-HMAC|", enc);
        Assert.DoesNotContain(plain, enc); // 密文不含明文
        Assert.Equal(plain, BundledCfKey.Decode(enc)); // Encrypted 静态字段由发布注入，往返走带参版
    }

    [Fact]
    public void Tampered_Cipher_ReturnsNull()
    {
        var enc = BundledCfKey.EncodeForBundling("some-key-value");
        Assert.NotNull(enc);
        // 翻转密文区一个字节（iv 之后）→ HMAC 校验失败 → 解密返回空
        var raw = Convert.FromBase64String(enc["AES-HMAC|".Length..]);
        raw[50] ^= 0x01;
        var tampered = "AES-HMAC|" + Convert.ToBase64String(raw);
        var result = BundledCfKey.Decode(tampered);
        Assert.Null(result);
    }

    [Fact]
    public void Empty_ReturnsNull()
    {
        Assert.Null(BundledCfKey.Decode());
        Assert.Null(BundledCfKey.Decode(""));
        Assert.Null(BundledCfKey.Decode("garbage"));
    }
}
