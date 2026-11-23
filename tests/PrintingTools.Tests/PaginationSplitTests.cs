using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using PrintingTools.Core;
using PrintingTools.Core.Pagination;
using Xunit;

namespace PrintingTools.Tests;

/// <summary>
/// Regression guard for split-page offsets: every slice must start at an exact multiple of the
/// available content height. The old maxOffset clamp pulled the last slice back to the content
/// bottom, so a visual that spilled only slightly over a page rendered a near-copy of the
/// previous page on the next one (double-column report bug from the consuming app).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class PaginationSplitTests
{
    [Fact]
    public void OverTallVisualSlicesAtExactMultiplesOfAvailableHeight()
    {
        using var headlessSession = HeadlessUnitTestSession.StartNew(typeof(PaginationSplitEntryPoint));

        headlessSession.Dispatch(() =>
        {
            var border = new Border
            {
                Width = 612,
                Height = 2600,
                Background = Brushes.White
            };

            border.Measure(new Size(border.Width, border.Height));
            border.Arrange(new Rect(0, 0, border.Width, border.Height));

            var document = PrintDocument.FromVisual(border, new PrintPageSettings
            {
                TargetSize = new Size(612, 2600),
                Margins = new Thickness(0)
            });

            var session = new PrintSession(document, new PrintOptions
            {
                PaperSize = new Size(8.5, 11),
                Margins = new Thickness(0),
                UsePrintableArea = false
            });

            var pages = session.Paginate(CancellationToken.None);

            Assert.True(pages.Count >= 3, $"Expected an over-tall visual to split into at least 3 pages, got {pages.Count}.");

            var first = pages[0].Metrics ?? PrintPageMetrics.Create(pages[0].Visual, pages[0].Settings);
            var scale = first.ContentScale <= 0 ? 1d : first.ContentScale;
            var availableHeight = first.ContentRect.Height / scale;
            Assert.True(availableHeight > 0);

            for (var i = 0; i < pages.Count; i++)
            {
                var metrics = pages[i].Metrics
                    ?? throw new InvalidOperationException($"Page {i} carries no metrics after pagination.");
                var expected = i * availableHeight;
                Assert.True(Math.Abs(metrics.ContentOffset.Y - expected) < 0.01,
                    $"Page {i} offset {metrics.ContentOffset.Y} is not the expected slice start {expected} (availableHeight {availableHeight}).");
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void VisualFittingOnOnePageIsNotSplit()
    {
        using var headlessSession = HeadlessUnitTestSession.StartNew(typeof(PaginationSplitEntryPoint));

        headlessSession.Dispatch(() =>
        {
            var border = new Border
            {
                Width = 612,
                Height = 500,
                Background = Brushes.White
            };

            border.Measure(new Size(border.Width, border.Height));
            border.Arrange(new Rect(0, 0, border.Width, border.Height));

            var document = PrintDocument.FromVisual(border, new PrintPageSettings
            {
                TargetSize = new Size(612, 500),
                Margins = new Thickness(0)
            });

            var session = new PrintSession(document, new PrintOptions
            {
                PaperSize = new Size(8.5, 11),
                Margins = new Thickness(0),
                UsePrintableArea = false
            });

            var pages = session.Paginate(CancellationToken.None);

            Assert.Single(pages);
        }, CancellationToken.None);
    }

    private sealed class PaginationSplitApplication : Application;

    private static class PaginationSplitEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder
                .Configure<PaginationSplitApplication>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions
                {
                    UseHeadlessDrawing = true
                });
    }
}