using System;
using PrintingTools.Core;
using PrintingTools.Windows;
using PrintingTools.Windows.Interop;
using Xunit;

namespace PrintingTools.Tests;

public class Win32LayoutDevModeTests
{
    [Fact]
    public void ApplyLayoutToDevMode_SetsNUpFlags()
    {
        var ticket = new PrintTicketModel();
        ticket.Extensions["layout.kind"] = PrintLayoutKind.NUp.ToString();
        ticket.Extensions["layout.nup.rows"] = "2";
        ticket.Extensions["layout.nup.columns"] = "2";
        var layout = LayoutMetadata.FromTicket(ticket);

        var devMode = default(Win32NativeMethods.DEVMODE);

        var modified = Win32PrintAdapter.ApplyLayoutToDevMode(layout, ref devMode);

        Assert.True(modified);
        Assert.Equal((uint)Win32NativeMethods.DM_NUP, devMode.dmFields & (uint)Win32NativeMethods.DM_NUP);
        Assert.Equal((uint)4, devMode.dmDisplayFlags);
    }

    [Fact]
    public void ApplyLayoutToDevMode_ClearsNUpForStandard()
    {
        var ticket = new PrintTicketModel();
        var layout = LayoutMetadata.FromTicket(ticket);

        var devMode = default(Win32NativeMethods.DEVMODE);
        devMode.dmFields = (uint)Win32NativeMethods.DM_NUP;
        devMode.dmDisplayFlags = 8;

        var modified = Win32PrintAdapter.ApplyLayoutToDevMode(layout, ref devMode);

        Assert.True(modified);
        Assert.Equal(0u, devMode.dmFields & (uint)Win32NativeMethods.DM_NUP);
        Assert.Equal((uint)0, devMode.dmDisplayFlags);
    }

    [Fact]
    public void ApplyLayoutToDevMode_PosterUsesTileCount()
    {
        var ticket = new PrintTicketModel();
        ticket.Extensions["layout.kind"] = PrintLayoutKind.Poster.ToString();
        ticket.Extensions["layout.poster.rows"] = "3";
        ticket.Extensions["layout.poster.columns"] = "2";
        var layout = LayoutMetadata.FromTicket(ticket);

        var devMode = default(Win32NativeMethods.DEVMODE);

        var modified = Win32PrintAdapter.ApplyLayoutToDevMode(layout, ref devMode);

        Assert.True(modified);
        Assert.Equal((uint)Win32NativeMethods.DM_NUP, devMode.dmFields & (uint)Win32NativeMethods.DM_NUP);
        Assert.Equal((uint)6, devMode.dmDisplayFlags);
    }

    [Fact]
    public void ApplyLayoutToDevMode_BookletShortEdgeBindsAsDuplexHorizontal()
    {
        var ticket = new PrintTicketModel();
        ticket.Extensions["layout.kind"] = PrintLayoutKind.Booklet.ToString();
        ticket.Extensions["layout.booklet.bindLongEdge"] = "0";
        var layout = LayoutMetadata.FromTicket(ticket);

        var devMode = default(Win32NativeMethods.DEVMODE);

        var modified = Win32PrintAdapter.ApplyLayoutToDevMode(layout, ref devMode);

        Assert.True(modified);
        Assert.Equal((uint)Win32NativeMethods.DM_DUPLEX, devMode.dmFields & (uint)Win32NativeMethods.DM_DUPLEX);
        // Literals, not constants: wingdi.h fixes DMDUP_VERTICAL=2 (long edge) and
        // DMDUP_HORIZONTAL=3 (short edge), so a re-sorted constant table must fail here.
        Assert.Equal((short)3, devMode.dmDuplex);
    }

    [Fact]
    public void ApplyLayoutToDevMode_BookletLongEdgeBindsAsDuplexVertical()
    {
        var ticket = new PrintTicketModel();
        ticket.Extensions["layout.kind"] = PrintLayoutKind.Booklet.ToString();
        ticket.Extensions["layout.booklet.bindLongEdge"] = "1";
        var layout = LayoutMetadata.FromTicket(ticket);

        var devMode = default(Win32NativeMethods.DEVMODE);

        Win32PrintAdapter.ApplyLayoutToDevMode(layout, ref devMode);

        Assert.Equal((short)2, devMode.dmDuplex);
    }

    [Fact]
    public void DocumentPropertiesNoPromptRefusesThePromptBit()
    {
        // The silent submission paths must never hand DocumentProperties a fMode that carries
        // DM_PROMPT (4): that bit is the driver's instruction to show its property sheet, which is
        // exactly the dialog silent printing is supposed to avoid.
        var ex = Assert.Throws<ArgumentException>(() =>
            Win32NativeMethods.DocumentPropertiesNoPrompt(
                IntPtr.Zero, IntPtr.Zero, "", IntPtr.Zero, IntPtr.Zero,
                Win32NativeMethods.DM_OUT_BUFFER | Win32NativeMethods.DM_PROMPT));

        Assert.Contains("DM_PROMPT", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyLayoutToDevMode_PinsStandardFormAndClearsDimensionBits()
    {
        // Default ticket media is Letter (612 x 792 pt): a known form must land on its
        // DMPAPER_* code with only DM_PAPERSIZE claimed, so the driver refills the edges.
        var layout = LayoutMetadata.FromTicket(new PrintTicketModel());

        var devMode = default(Win32NativeMethods.DEVMODE);
        devMode.dmFields = (uint)(Win32NativeMethods.DM_PAPERWIDTH | Win32NativeMethods.DM_PAPERLENGTH);
        devMode.dmPaperWidth = 1234;
        devMode.dmPaperLength = 5678;

        var modified = Win32PrintAdapter.ApplyLayoutToDevMode(layout, ref devMode);

        Assert.True(modified);
        Assert.Equal((uint)Win32NativeMethods.DM_PAPERSIZE, devMode.dmFields & (uint)Win32NativeMethods.DM_PAPERSIZE);
        Assert.Equal(0u, devMode.dmFields & (uint)(Win32NativeMethods.DM_PAPERWIDTH | Win32NativeMethods.DM_PAPERLENGTH));
        Assert.Equal(Win32NativeMethods.DMPAPER_LETTER, devMode.dmPaperSize);
    }

    [Theory]
    [InlineData(595d, 842d, (short)9)]   // A4 portrait in points (420 x 595 would be A5)
    [InlineData(842d, 595d, (short)9)]   // same sheet pre-swapped for landscape matches the same form
    [InlineData(420d, 595d, (short)11)]  // A5
    [InlineData(842d, 1191d, (short)8)]  // A3
    public void ApplyLayoutToDevMode_MatchesIsoFormsRegardlessOfEdgeOrder(double width, double height, short expected)
    {
        var ticket = new PrintTicketModel(new PageMediaSize("iso", width, height));
        var layout = LayoutMetadata.FromTicket(ticket);

        var devMode = default(Win32NativeMethods.DEVMODE);

        Win32PrintAdapter.ApplyLayoutToDevMode(layout, ref devMode);

        Assert.Equal(expected, devMode.dmPaperSize);
        Assert.Equal((uint)Win32NativeMethods.DM_PAPERSIZE, devMode.dmFields & (uint)Win32NativeMethods.DM_PAPERSIZE);
    }

    [Fact]
    public void ApplyLayoutToDevMode_UnknownMediaBecomesUserFormWithTenthsOfMillimetres()
    {
        var ticket = new PrintTicketModel(new PageMediaSize("Custom", 120d, 200d, unit: "Millimeters"));
        var layout = LayoutMetadata.FromTicket(ticket);

        var devMode = default(Win32NativeMethods.DEVMODE);

        var modified = Win32PrintAdapter.ApplyLayoutToDevMode(layout, ref devMode);

        Assert.True(modified);
        Assert.Equal(Win32NativeMethods.DMPAPER_USER, devMode.dmPaperSize);
        Assert.Equal(1200, devMode.dmPaperWidth);
        Assert.Equal(2000, devMode.dmPaperLength);
        Assert.Equal(
            (uint)(Win32NativeMethods.DM_PAPERSIZE | Win32NativeMethods.DM_PAPERWIDTH | Win32NativeMethods.DM_PAPERLENGTH),
            devMode.dmFields & (uint)(Win32NativeMethods.DM_PAPERSIZE | Win32NativeMethods.DM_PAPERWIDTH | Win32NativeMethods.DM_PAPERLENGTH));
    }

    [Fact]
    public void ApplyLayoutToDevMode_UnrecognisedUnitLeavesPaperUntouched()
    {
        // No guessing: an unit the mapper cannot convert must not rewrite the paper fields.
        var ticket = new PrintTicketModel(new PageMediaSize("odd", 100d, 200d, unit: "Picas"));
        var layout = LayoutMetadata.FromTicket(ticket);

        var devMode = default(Win32NativeMethods.DEVMODE);
        devMode.dmPaperSize = 42;

        Win32PrintAdapter.ApplyLayoutToDevMode(layout, ref devMode);

        Assert.Equal(42, devMode.dmPaperSize);
        Assert.Equal(0u, devMode.dmFields & (uint)(Win32NativeMethods.DM_PAPERSIZE | Win32NativeMethods.DM_PAPERWIDTH | Win32NativeMethods.DM_PAPERLENGTH));
    }

    [Fact]
    public void ApplyLayoutToDevMode_LayoutWithoutMediaLeavesPaperUntouched()
    {
        // default(LayoutMetadata) carries no media (pre-MediaSize callers): paper stays as-is.
        var layout = default(LayoutMetadata);

        var devMode = default(Win32NativeMethods.DEVMODE);
        devMode.dmPaperSize = 42;

        Win32PrintAdapter.ApplyLayoutToDevMode(layout, ref devMode);

        Assert.Equal(42, devMode.dmPaperSize);
        Assert.Equal(0u, devMode.dmFields & (uint)Win32NativeMethods.DM_PAPERSIZE);
    }
}
