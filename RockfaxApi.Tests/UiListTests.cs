using System.Runtime.InteropServices;
using RockfaxDesk;
using Xunit;

namespace RockfaxApi.Tests;

/// <summary>UiList render machinery (the flicker fix lives in the native style).</summary>
public class UiListTests
{
    private const int LvmGetExtendedStyle = 0x1037;   // LVM_GETEXTENDEDLISTVIEWSTYLE
    private const int LvsExDoubleBuffer = 0x00010000; // LVS_EX_DOUBLEBUFFER

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [Fact]
    public void Lists_paint_through_a_double_buffer() => RunSta(() =>
    {
        using var list = new UiList();
        _ = list.Handle; // forces creation, which applies the extended style
        // (The getter returns every extended bit — LVS_EX_FULLROWSELECT is expected too.)
        long bits = SendMessage(list.Handle, LvmGetExtendedStyle, (IntPtr)LvsExDoubleBuffer, IntPtr.Zero).ToInt64();
        Assert.True((bits & LvsExDoubleBuffer) != 0,
            $"LVS_EX_DOUBLEBUFFER not set on UiList handle (extended styles: 0x{bits:X})");
    });

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { error = e; }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "test thread timed out");
        if (error is not null) throw error;
    }
}
