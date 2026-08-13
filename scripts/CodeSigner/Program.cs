using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

// 权威 Authenticode 签名工具（绕过 PowerShell 5.1 Set-AuthenticodeSignature 的 PE 预解析 bug——
// 它对「apphost 区段小 + 单文件 bundle 附加大」的文件算错证书表偏移，报"非 Win32 应用"假失败）。
// 直接 P/Invoke SignerSignEx（API 自行定位证书表）。用法: CodeSigner.exe <文件> [时间戳URL]

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 1) { Console.Error.WriteLine("用法: CodeSigner.exe <文件> [时间戳URL]"); return 2; }
        var path = Path.GetFullPath(args[0]);
        var tsUrl = args.Length > 1 ? args[1] : "http://timestamp.digicert.com";

        var cert = FindCert();
        if (cert is null) { Console.Error.WriteLine("未找到 LauncherDev 签名证书"); return 3; }
        using (cert)
        {
            try
            {
                var hr = Sign(path, cert, tsUrl);
                if (hr != 0) { Console.Error.WriteLine($"签名失败 HRESULT=0x{hr:X8}（{(hr == 0x80070001 ? "文件无效（PE 解析失败）" : "")}）"); return 1; }
                Console.WriteLine($"[CodeSigner] 已签名: {Path.GetFileName(path)}");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine($"签名异常: {ex.Message}"); return 1; }
        }
    }

    private static X509Certificate2? FindCert()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        return store.Certificates.Find(X509FindType.FindBySubjectName, "LauncherDev", false)
            .OfType<X509Certificate2>().FirstOrDefault();
    }

    private static int Sign(string path, X509Certificate2 cert, string tsUrl)
    {
        var certCtx = cert.Handle; // CERT_CONTEXT*（IntPtr）

        // SIGNER_CERT_STORE_INFO → SIGNER_CERT → SIGNER_SIGN_EX_INFO 三层全部 AllocHGlobal 指针传递
        // （ref 传栈 struct 在 SignerSignEx 下会崩——指针化最稳）
        var pCertStoreInfo = Marshal.AllocHGlobal(Marshal.SizeOf<SIGNER_CERT_STORE_INFO>());
        var pSignerCert = Marshal.AllocHGlobal(Marshal.SizeOf<SIGNER_CERT>());
        var pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<SIGNER_FILE_INFO>());
        var pSignExInfo = Marshal.AllocHGlobal(Marshal.SizeOf<SIGNER_SIGN_EX_INFO>());
        var pFileName = Marshal.StringToHGlobalUni(path);

        try
        {
            var certStoreInfo = new SIGNER_CERT_STORE_INFO
            {
                cbSize = Marshal.SizeOf<SIGNER_CERT_STORE_INFO>(),
                dwCertPolicy = 2, // SIGNER_CERT_POLICY_CHAIN
                pSigningCert = certCtx,
            };
            Marshal.StructureToPtr(certStoreInfo, pCertStoreInfo, false);

            var signerCert = new SIGNER_CERT
            {
                cbSize = Marshal.SizeOf<SIGNER_CERT>(),
                dwCertChoice = 2, // SIGNER_CERT_STORE
                pCertStore = pCertStoreInfo,
            };
            Marshal.StructureToPtr(signerCert, pSignerCert, false);

            var fileInfo = new SIGNER_FILE_INFO
            {
                cbSize = Marshal.SizeOf<SIGNER_FILE_INFO>(),
                pwszFileName = pFileName,
            };
            Marshal.StructureToPtr(fileInfo, pFileInfo, false);

            var signExInfo = new SIGNER_SIGN_EX_INFO
            {
                cbSize = Marshal.SizeOf<SIGNER_SIGN_EX_INFO>(),
                pSignerCert = pSignerCert,
            };
            Marshal.StructureToPtr(signExInfo, pSignExInfo, false);

            return SignerSignEx(0, null, tsUrl, 0, pFileInfo, pSignerCert, pSignExInfo);
        }
        finally
        {
            Marshal.FreeHGlobal(pCertStoreInfo);
            Marshal.FreeHGlobal(pSignerCert);
            Marshal.FreeHGlobal(pFileInfo);
            Marshal.FreeHGlobal(pSignExInfo);
            Marshal.FreeHGlobal(pFileName);
        }
    }

    // ---------- 结构定义（与 signtool 的 SignerSignEx 一致） ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct SIGNER_FILE_INFO
    {
        public int cbSize;
        public IntPtr pwszFileName;
        public IntPtr hwnd;
        public IntPtr pwszSubjectName;
        public int dwSigningFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIGNER_CERT_STORE_INFO
    {
        public int cbSize;
        public IntPtr pSigningCert;   // CERT_CONTEXT*
        public int dwCertPolicy;
        public IntPtr hCertStore;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIGNER_CERT
    {
        public int cbSize;
        public int dwCertChoice;      // 2 = SIGNER_CERT_STORE
        public IntPtr pCertStore;     // SIGNER_CERT_STORE_INFO*
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIGNER_SIGN_EX_INFO
    {
        public int cbSize;
        public IntPtr pSignerCert;    // SIGNER_CERT*
        public int dwFlags;
        public IntPtr pwszDescription;
        public IntPtr pwszMoreInfo;
    }

    [DllImport("Mssign32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int SignerSignEx(
        int dwFlags, string? pwszSubjectName, string? pwszTimestampUrl,
        int dwIndex, IntPtr pFileInfo, IntPtr pSignerCert, IntPtr pSignerExInfo);
}
