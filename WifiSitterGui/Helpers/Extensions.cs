using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace WifiSitterGui.Helpers
{
    public static class Extensions
    {
        public static void SetRtf(this RichTextBox rtb, string document) {
            var documentBytes = Encoding.UTF8.GetBytes(document);
            using (var reader = new MemoryStream(documentBytes)) {
                reader.Position = 0;
                rtb.SelectAll();
                rtb.Selection.Load(reader, DataFormats.Rtf);
                rtb.Selection.Select(rtb.Selection.Start, rtb.Selection.Start);
            }
        }

        public static void SetRtf(this RichTextBox rtb, Uri resource) {
            var sInfo = Application.GetResourceStream(resource);
            using (var bReader = new BinaryReader(sInfo.Stream)) {
                var documentBytes = bReader.ReadBytes((int)sInfo.Stream.Length);
                using (var reader = new MemoryStream(documentBytes)) {
                    reader.Position = 0;
                    new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd)
                        .Load(reader, DataFormats.Rtf);
                }
            }
        }

        public static bool SoonerThan(this DateTime then, DateTime now) {
            return then > now;
        }
    }
}
