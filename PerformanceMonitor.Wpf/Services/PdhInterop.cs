using System.Runtime.InteropServices;
using System.Text;

namespace PerformanceMonitor.Wpf.Services;

/// <summary>
/// PDH (Performance Data Helper) P/Invoke。
/// 使用 PdhAddEnglishCounterW 保证在本地化（中文等）Windows 上计数器名不受影响。
/// </summary>
internal static class PdhInterop
{
    private const uint PdhFmtDouble = 0x00000200;

    /// <summary>原生 PDH_FMT_COUNTERVALUE：CStatus + 值联合体（double 分支）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FmtCounterValue
    {
        public uint CStatus;
        public double DoubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQueryW(string dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounterW(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern int PdhRemoveCounter(IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr counterType, out FmtCounterValue value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhExpandWildCardPathW(string machineName, string path, StringBuilder buffer, ref uint size, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryW(string fileName);

    /// <summary>打开的查询句柄集合。</summary>
    public sealed class Query : IDisposable
    {
        private IntPtr _query;
        public bool Ok { get; private set; }

        public bool Open()
        {
            try
            {
                Ok = PdhOpenQueryW(null, IntPtr.Zero, out _query) == 0;
            }
            catch
            {
                Ok = false;
            }
            return Ok;
        }

        public IntPtr AddEnglish(string path)
        {
            if (!Ok) return IntPtr.Zero;
            return PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out IntPtr h) == 0 ? h : IntPtr.Zero;
        }

        public void Collect()
        {
            if (Ok) PdhCollectQueryData(_query);
        }

        public bool Remove(IntPtr handle)
        {
            return Ok && handle != IntPtr.Zero && PdhRemoveCounter(handle) == 0;
        }

        public double Read(IntPtr handle)
        {
            if (handle == IntPtr.Zero || !Ok) return double.NaN;
            // CStatus == ERROR_SUCCESS(0) 时 doubleValue 才有效
            return PdhGetFormattedCounterValue(handle, PdhFmtDouble, IntPtr.Zero, out FmtCounterValue v) == 0 && v.CStatus == 0
                ? v.DoubleValue
                : double.NaN;
        }

        public void Dispose()
        {
            // 无独立关闭 API 需求（PdhCloseQuery 可选），进程生命周期内保留即可。
            Ok = false;
        }
    }

    /// <summary>
    /// 展开通配符路径，返回完整路径列表。失败返回空列表。
    /// </summary>
    public static List<string> ExpandWildCard(string path)
    {
        var result = new List<string>();
        try
        {
            uint size = 0;
            PdhExpandWildCardPathW(null, path, null, ref size, 0);
            if (size == 0) return result;

            var sb = new StringBuilder((int)size);
            if (PdhExpandWildCardPathW(null, path, sb, ref size, 0) != 0) return result;

            foreach (var p in sb.ToString().Split('\0'))
            {
                var t = p.Trim();
                if (t.Length > 0) result.Add(t);
            }
        }
        catch
        {
        }
        return result;
    }

    /// <summary>从展开的路径中提取实例名（括号内部分）。</summary>
    public static string ExtractInstance(string fullPath)
    {
        int open = fullPath.IndexOf('(');
        int close = fullPath.LastIndexOf(')');
        if (open < 0 || close <= open) return "";
        return fullPath.Substring(open + 1, close - open - 1);
    }

    /// <summary>预加载 pdh.dll，确保可用性。</summary>
    public static void Warmup() => LoadLibraryW("pdh.dll");
}
