using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NINA.Profile;
using NINA.Profile.Interfaces;

namespace FitsMetadataForCr2 {

    /// <summary>Plugin options, stored per N.I.N.A. profile.</summary>
    public class PluginSettings : INotifyPropertyChanged {
        public static readonly Guid PluginGuid = Guid.Parse("0f087026-c042-4035-8f14-0dc2580e9b25");

        private readonly PluginOptionsAccessor accessor;

        public PluginSettings(IProfileService profileService) {
            accessor = new PluginOptionsAccessor(profileService, PluginGuid);
        }

        /// <summary>Write the FITS keywords into the raw file itself</summary>
        public bool EmbedInRawFile {
            get => accessor.GetValueBoolean(nameof(EmbedInRawFile), true);
            set { accessor.SetValueBoolean(nameof(EmbedInRawFile), value); RaisePropertyChanged(); }
        }

        /// <summary>Also write them to an .xmp sidecar next to the raw file</summary>
        public bool WriteSidecar {
            get => accessor.GetValueBoolean(nameof(WriteSidecar), false);
            set { accessor.SetValueBoolean(nameof(WriteSidecar), value); RaisePropertyChanged(); }
        }

        public void RaiseAllChanged() => RaisePropertyChanged(string.Empty);

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
