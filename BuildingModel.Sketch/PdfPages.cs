using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// Reads PDF pages with the PDF renderer built into Windows 10/11 (Windows.Data.Pdf): the paper size of each page
    /// and a page rendered to a PNG at a chosen resolution. Only the Windows metadata present on every Windows 10/11
    /// machine is referenced (System32\WinMetadata); no SDK or package. The methods block while Windows works:
    /// call them through <see cref="InBackground{T}"/>.
    /// </summary>
    public static class PdfPages
    {
        /// <summary>The paper size of every page, inches (width, height), as the page is shown (rotation applied).</summary>
        public static List<(double width, double height)> PaperSizes(string path)
        {
            var list = new List<(double, double)>();
            using (var ras = ToStream(File.ReadAllBytes(path)))
            {
                var doc = Load(ras);
                for (uint i = 0; i < doc.PageCount; i++)
                    using (var page = doc.GetPage(i))
                        list.Add((page.Size.Width / Dips, page.Size.Height / Dips));
            }
            return list;
        }

        /// <summary>One page rendered to PNG bytes at <paramref name="dpi"/> pixels per inch of paper.</summary>
        public static byte[] RenderPng(string path, int pageIndex, double dpi)
        {
            using (var ras = ToStream(File.ReadAllBytes(path)))
            {
                var doc = Load(ras);
                if (pageIndex < 0 || pageIndex >= doc.PageCount) throw new ArgumentOutOfRangeException(nameof(pageIndex));
                using (var page = doc.GetPage((uint)pageIndex))
                using (var output = new InMemoryRandomAccessStream())
                {
                    var options = new PdfPageRenderOptions
                    {
                        DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width / Dips * dpi)),
                        DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height / Dips * dpi)),
                    };
                    Wait(page.RenderToStreamAsync(output, options)); // PNG by default
                    return ReadAll(output);
                }
            }
        }

        /// <summary>The resolution for a page: fine enough to read, small enough to keep (longest side at most 6000 pixels, at most 200 dpi).</summary>
        public static double RenderDpi(double widthInches, double heightInches) =>
            Math.Max(50, Math.Min(200, 6000 / Math.Max(1e-6, Math.Max(widthInches, heightInches))));

        /// <summary>Runs <paramref name="work"/> off the UI thread.</summary>
        public static Task<T> InBackground<T>(Func<T> work) => Task.Run(work);

        /// <summary>PdfPage.Size is in device-independent pixels (1/96 inch).</summary>
        private const double Dips = 96.0;

        private static PdfDocument Load(IRandomAccessStream ras)
        {
            try
            {
                return Wait(PdfDocument.LoadFromStreamAsync(ras));
            }
            catch (Exception ex) when (!(ex is IOException))
            {
                throw new InvalidDataException("Windows could not open this PDF (it may be damaged or password-protected). " + ex.Message, ex);
            }
        }

        private static IRandomAccessStream ToStream(byte[] bytes)
        {
            var ras = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(ras.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                Wait(writer.StoreAsync());
                Wait(writer.FlushAsync());
                writer.DetachStream();
            }
            ras.Seek(0);
            return ras;
        }

        private static byte[] ReadAll(IRandomAccessStream ras)
        {
            ras.Seek(0);
            var bytes = new byte[ras.Size];
            using (var reader = new DataReader(ras.GetInputStreamAt(0)))
            {
                Wait(reader.LoadAsync((uint)ras.Size));
                reader.ReadBytes(bytes);
                reader.DetachStream();
            }
            return bytes;
        }

        private const int TimeoutMs = 120000;

        private static T Wait<T>(IAsyncOperation<T> op)
        {
            WaitFor(op);
            return op.GetResults();
        }

        private static void Wait(IAsyncAction op)
        {
            WaitFor(op);
            op.GetResults();
        }

        private static void WaitFor(IAsyncInfo op)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (op.Status == AsyncStatus.Started)
            {
                if (sw.ElapsedMilliseconds > TimeoutMs) { op.Cancel(); throw new TimeoutException("Windows took too long to read the PDF."); }
                Thread.Sleep(5);
            }
            if (op.Status == AsyncStatus.Error) throw op.ErrorCode ?? new InvalidDataException("Windows could not read the PDF.");
            if (op.Status == AsyncStatus.Canceled) throw new OperationCanceledException();
        }
    }
}
