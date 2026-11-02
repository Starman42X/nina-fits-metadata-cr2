using System.Reflection;
using System.Runtime.InteropServices;

// [Guid] is the plugin identifier. Keep it stable across releases.
[assembly: Guid("0f087026-c042-4035-8f14-0dc2580e9b25")]

[assembly: AssemblyTitle("FITS Metadata for CR2")]
[assembly: AssemblyDescription("Writes the FITS header N.I.N.A. would put into a FITS file (target, coordinates, site, telescope, filter, focuser, weather, ...) into Canon CR2 raw files as XMP metadata.")]
[assembly: AssemblyCompany("Louis")]
[assembly: AssemblyProduct("FITS Metadata for CR2")]
[assembly: AssemblyCopyright("Copyright © 2026")]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.2.0.9001")]

[assembly: AssemblyMetadata("License", "MPL-2.0")]
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
[assembly: AssemblyMetadata("Repository", "")]
[assembly: AssemblyMetadata("Homepage", "")]
[assembly: AssemblyMetadata("Tags", "DSLR,Canon,CR2,RAW,FITS,Metadata,XMP")]
[assembly: AssemblyMetadata("ChangelogURL", "")]
[assembly: AssemblyMetadata("FeaturedImageURL", "")]
[assembly: AssemblyMetadata("ScreenshotURL", "")]
[assembly: AssemblyMetadata("AltScreenshotURL", "")]
[assembly: AssemblyMetadata("LongDescription", @"When N.I.N.A. saves DSLR images in the camera's native RAW format (Options > Imaging > File format: RAW), the file only contains what the camera knows: exposure, ISO, date. Everything N.I.N.A. knows about the frame - target name and coordinates, image type, site location, telescope and focal length, filter, focuser position and temperature, sensor temperature, rotator angle, pier side, airmass, weather data and headers added by other plugins - normally only ends up in FITS/XISF files.

This plugin writes exactly those FITS keywords into each saved CR2 file as XMP metadata (namespace prefix 'fits', e.g. fits:OBJECT, fits:RA, fits:FOCALLEN). The target name is also written as the title, and the site as standard GPS coordinates, so photo tools show them too.

* The raw image data is never touched. Canon reserves space for XMP in every CR2; the metadata is written into that space. Only if it does not fit is it appended at the end of the file
* Existing XMP (e.g. the star rating set in camera) is kept
* Optionally also writes an .xmp sidecar file
* Also works for other TIFF-based raw formats (NEF, ARW, DNG, PEF). CR3 files are not TIFF-based: use the sidecar option for them

Read the metadata with e.g. exiftool -XMP-fits:all IMG_0001.CR2")]

[assembly: ComVisible(false)]
