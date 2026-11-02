using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NINA.Core.Utility;
using NINA.Image.ImageData;

namespace FitsMetadataForCr2 {

    /// <summary>What gets written: the FITS keywords, plus the target and site in standard XMP properties.</summary>
    public record XmpContent(IReadOnlyList<FitsKeyword> Keywords, string Title, double Latitude, double Longitude, double Elevation) {

        public static XmpContent FromMetaData(ImageMetaData metaData) => new(
            FitsKeywords.FromMetaData(metaData),
            metaData.Target?.Name,
            metaData.Observer?.Latitude ?? double.NaN,
            metaData.Observer?.Longitude ?? double.NaN,
            metaData.Observer?.Elevation ?? double.NaN);
    }

    public static class XmpPacket {
        public const string FitsNamespace = "urn:x-fits-metadata:1.0/";

        private static readonly XNamespace x = "adobe:ns:meta/";
        private static readonly XNamespace rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        private static readonly XNamespace dc = "http://purl.org/dc/elements/1.1/";
        private static readonly XNamespace exif = "http://ns.adobe.com/exif/1.0/";
        private static readonly XNamespace fits = FitsNamespace;

        private const string PacketHeader = "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>";
        private const string PacketTrailer = "<?xpacket end=\"w\"?>";

        /// <summary>
        /// Returns the x:xmpmeta element for <paramref name="existingXmp"/> with this plugin's properties replaced by
        /// <paramref name="content"/>. Everything else in the existing XMP (e.g. the camera's rating) is kept.
        /// </summary>
        public static string Merge(string existingXmp, XmpContent content) {
            var description = BuildDescription(content);
            var written = new HashSet<XName>(description.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name != rdf + "about").Select(a => a.Name)
                .Concat(description.Elements().Select(e => e.Name)));
            bool IsOurs(XName name) => name.Namespace == fits || written.Contains(name);

            var rdfRoot = ParseRdf(existingXmp) ?? new XElement(rdf + "RDF", new XAttribute(XNamespace.Xmlns + "rdf", rdf));
            foreach (var d in rdfRoot.Elements(rdf + "Description").ToList()) {
                d.Attributes().Where(a => !a.IsNamespaceDeclaration && IsOurs(a.Name)).Remove();
                d.Elements().Where(e => IsOurs(e.Name)).Remove();
                if (!d.HasElements && !d.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name != rdf + "about")) d.Remove();
            }
            rdfRoot.Add(description);

            var meta = new XElement(x + "xmpmeta",
                new XAttribute(XNamespace.Xmlns + "x", x),
                new XAttribute(x + "xmptk", "N.I.N.A. FITS Metadata for CR2"),
                rdfRoot);
            return meta.ToString(SaveOptions.DisableFormatting);
        }

        /// <summary>Smallest packet size that holds <paramref name="xmpMeta"/>.</summary>
        public static int MinimumLength(string xmpMeta) => Encoding.UTF8.GetByteCount(PacketHeader + xmpMeta + PacketTrailer);

        /// <summary>
        /// Wraps <paramref name="xmpMeta"/> in an XMP packet of exactly <paramref name="totalLength"/> bytes,
        /// padded with whitespace so it can be edited in place later. Null if it does not fit.
        /// </summary>
        public static byte[] ToPacket(string xmpMeta, int totalLength) {
            var body = Encoding.UTF8.GetBytes(PacketHeader + xmpMeta);
            var trailer = Encoding.UTF8.GetBytes(PacketTrailer);
            if (body.Length + trailer.Length > totalLength) return null;

            var packet = new byte[totalLength];
            body.CopyTo(packet, 0);
            for (var i = body.Length; i < totalLength - trailer.Length; i++) {
                packet[i] = (byte)((i - body.Length) % 100 == 99 ? '\n' : ' ');
            }
            trailer.CopyTo(packet, totalLength - trailer.Length);
            return packet;
        }

        private static XElement BuildDescription(XmpContent content) {
            var d = new XElement(rdf + "Description",
                new XAttribute(rdf + "about", string.Empty),
                new XAttribute(XNamespace.Xmlns + "fits", fits));

            // One property per keyword; keywords that occur more than once (COMMENT, HISTORY) become a list
            foreach (var group in content.Keywords.GroupBy(k => k.Key)) {
                var name = fits + XmlConvert.EncodeLocalName(group.Key);
                if (group.Count() == 1) {
                    d.Add(new XAttribute(name, group.First().Value));
                } else {
                    d.Add(new XElement(name, new XElement(rdf + "Seq", group.Select(k => new XElement(rdf + "li", k.Value)))));
                }
            }

            if (!double.IsNaN(content.Latitude) && !double.IsNaN(content.Longitude) && !(content.Latitude == 0 && content.Longitude == 0)) {
                d.Add(new XAttribute(XNamespace.Xmlns + "exif", exif),
                    new XAttribute(exif + "GPSVersionID", "2.2.0.0"),
                    new XAttribute(exif + "GPSLatitude", GpsCoordinate(content.Latitude, 'N', 'S')),
                    new XAttribute(exif + "GPSLongitude", GpsCoordinate(content.Longitude, 'E', 'W')));
                if (!double.IsNaN(content.Elevation)) {
                    d.Add(new XAttribute(exif + "GPSAltitude", FormattableString.Invariant($"{Math.Round(Math.Abs(content.Elevation) * 100)}/100")),
                        new XAttribute(exif + "GPSAltitudeRef", content.Elevation < 0 ? "1" : "0"));
                }
            }

            if (!string.IsNullOrWhiteSpace(content.Title)) {
                d.Add(new XAttribute(XNamespace.Xmlns + "dc", dc),
                    new XElement(dc + "title", new XElement(rdf + "Alt",
                        new XElement(rdf + "li", new XAttribute(XNamespace.Xml + "lang", "x-default"), content.Title))));
            }
            return d;
        }

        // XMP GPSCoordinate: "DDD,MM.mmmmmmR"
        private static string GpsCoordinate(double degrees, char positive, char negative) {
            var abs = Math.Abs(degrees);
            var whole = Math.Floor(abs);
            return string.Format(CultureInfo.InvariantCulture, "{0},{1:0.000000}{2}", whole, (abs - whole) * 60, degrees < 0 ? negative : positive);
        }

        private static XElement ParseRdf(string xmp) {
            if (string.IsNullOrWhiteSpace(xmp)) return null;
            try {
                var doc = XDocument.Parse(xmp.Trim('\0', ' ', '\t', '\r', '\n'));
                var rdfRoot = doc.Descendants(rdf + "RDF").FirstOrDefault();
                rdfRoot?.Remove();
                return rdfRoot;
            } catch (XmlException ex) {
                Logger.Warning($"FITS Metadata for CR2: existing XMP could not be read and is replaced: {ex.Message}");
                return null;
            }
        }
    }
}
