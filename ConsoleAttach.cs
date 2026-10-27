using System.Runtime.InteropServices;
using System.Text;

namespace TintaStudio;

/// <summary>
/// Penempel konsol induk untuk mode CLI di Windows (Tahap 6 —
/// packaging). .exe diterbitkan sebagai WinExe (GUI), jadi saat
/// dipanggil dari terminal, proses TIDAK mewarisi konsol pemanggil
/// dan Console.WriteLine hilang. Solusinya: bila ada argumen CLI
/// dan platformnya Windows, tempelkan proses ke konsol induk
/// (AttachConsole(ATTACH_PARENT_PROCESS)) lalu buka ulang aliran
/// standar. Di platform selain Windows fungsi ini no-op total
/// (P/Invoke tidak pernah dipanggil); mode GUI tidak terpengaruh.
/// </summary>
internal static class ConsoleAttach
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);

    /// <summary>Tempel ke konsol proses induk bila berjalan di
    /// Windows. Aman dipanggil di mana saja; kegagalan diabaikan
    /// (CLI tetap berjalan, hanya tanpa keluaran konsol).</summary>
    public static void TryAttachParentConsole()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            if (!AttachConsole(AttachParentProcess))
                return;
            var stdout = new StreamWriter(Console.OpenStandardOutput())
            {
                AutoFlush = true,
            };
            Console.SetOut(stdout);
            var stderr = new StreamWriter(Console.OpenStandardError())
            {
                AutoFlush = true,
            };
            Console.SetError(stderr);
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (Exception)
        {
            // Konsol tidak bisa ditempel (mis. dipanggil dari GUI) —
            // bukan kesalahan fatal.
        }
    }
}
