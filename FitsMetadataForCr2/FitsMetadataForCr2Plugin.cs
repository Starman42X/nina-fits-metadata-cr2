using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;

namespace FitsMetadataForCr2 {

    [Export(typeof(IPluginManifest))]
    public class FitsMetadataForCr2Plugin : PluginBase, INotifyPropertyChanged {
        private readonly IProfileService profileService;
        private readonly IImageSaveMediator imageSaveMediator;
        private readonly SemaphoreSlim writeLock = new(1, 1);
        private INotifyPropertyChanged watchedFileSettings;
        private string status = "No raw file saved yet";

        [ImportingConstructor]
        public FitsMetadataForCr2Plugin(IProfileService profileService, IImageSaveMediator imageSaveMediator) {
            this.profileService = profileService;
            this.imageSaveMediator = imageSaveMediator;
            Settings = new PluginSettings(profileService);

            imageSaveMediator.ImageSaved += ImageSaveMediator_ImageSaved;
            profileService.ProfileChanged += ProfileService_ProfileChanged;
            WatchFileSettings();
        }

        public PluginSettings Settings { get; }

        public string Status {
            get => status;
            private set { status = value; RaisePropertyChanged(); }
        }

        /// <summary>Raw files are only saved when N.I.N.A.'s file format is RAW; the options page warns otherwise</summary>
        public bool IsRawFileTypeSelected => profileService.ActiveProfile?.ImageFileSettings?.FileType == FileTypeEnum.RAW;

        public override Task Teardown() {
            imageSaveMediator.ImageSaved -= ImageSaveMediator_ImageSaved;
            profileService.ProfileChanged -= ProfileService_ProfileChanged;
            if (watchedFileSettings != null) watchedFileSettings.PropertyChanged -= FileSettings_PropertyChanged;
            return base.Teardown();
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            Settings.RaiseAllChanged();
            WatchFileSettings();
        }

        private void WatchFileSettings() {
            if (watchedFileSettings != null) watchedFileSettings.PropertyChanged -= FileSettings_PropertyChanged;
            watchedFileSettings = profileService.ActiveProfile?.ImageFileSettings as INotifyPropertyChanged;
            if (watchedFileSettings != null) watchedFileSettings.PropertyChanged += FileSettings_PropertyChanged;
            RaisePropertyChanged(nameof(IsRawFileTypeSelected));
        }

        private void FileSettings_PropertyChanged(object sender, PropertyChangedEventArgs e) =>
            RaisePropertyChanged(nameof(IsRawFileTypeSelected));

        private void ImageSaveMediator_ImageSaved(object sender, ImageSavedEventArgs e) {
            try {
                if (e.FileType != FileTypeEnum.RAW || e.PathToImage == null || e.MetaData == null) return;
                var embed = Settings.EmbedInRawFile;
                var sidecar = Settings.WriteSidecar;
                if (!embed && !sidecar) return;

                var content = XmpContent.FromMetaData(e.MetaData);
                _ = Task.Run(() => WriteAsync(e.PathToImage.LocalPath, content, embed, sidecar));
            } catch (Exception ex) {
                Logger.Error("FITS Metadata for CR2: could not read the image metadata", ex);
            }
        }

        private async Task WriteAsync(string path, XmpContent content, bool embed, bool sidecar) {
            await writeLock.WaitAsync();
            try {
                var name = Path.GetFileName(path);
                var done = new List<string>();
                if (embed) {
                    if (TiffXmpWriter.IsTiffBased(path)) {
                        var method = await RetryAsync(() => TiffXmpWriter.Embed(path, existing => XmpPacket.Merge(existing, content)));
                        done.Add(method == EmbedMethod.InPlace ? "embedded" : "embedded (appended to file)");
                    } else {
                        Logger.Warning($"FITS Metadata for CR2: {name} is not a TIFF-based raw file (e.g. CR3), metadata not embedded. Use the .xmp sidecar option for it.");
                        done.Add("not embedded (not a TIFF-based raw file)");
                    }
                }
                if (sidecar) {
                    await RetryAsync(() => WriteSidecar(path, content));
                    done.Add("sidecar written");
                }
                Status = $"{DateTime.Now:HH:mm:ss} {name}: {content.Keywords.Count} FITS keywords {string.Join(", ", done)}";
                Logger.Info($"FITS Metadata for CR2: {name}: {content.Keywords.Count} FITS keywords {string.Join(", ", done)}");
            } catch (Exception ex) {
                Logger.Error($"FITS Metadata for CR2: could not write metadata for {path}", ex);
                Status = $"Failed {DateTime.Now:HH:mm:ss}: {Path.GetFileName(path)}: {ex.Message}";
                Notification.ShowWarning($"FITS Metadata for CR2: {Path.GetFileName(path)}: {ex.Message}");
            } finally {
                writeLock.Release();
            }
        }

        public static bool WriteSidecar(string rawPath, XmpContent content) {
            var sidecar = Path.ChangeExtension(rawPath, ".xmp");
            var existing = File.Exists(sidecar) ? File.ReadAllText(sidecar) : null;
            var xmpMeta = XmpPacket.Merge(existing, content);
            File.WriteAllBytes(sidecar, XmpPacket.ToPacket(xmpMeta, XmpPacket.MinimumLength(xmpMeta) + 2048));
            return true;
        }

        // The file may still be open briefly (e.g. another plugin reading it right after it was saved)
        private static async Task<T> RetryAsync<T>(Func<T> action) {
            for (var attempt = 1; ; attempt++) {
                try {
                    return action();
                } catch (IOException) when (attempt < 5) {
                    await Task.Delay(1000);
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
