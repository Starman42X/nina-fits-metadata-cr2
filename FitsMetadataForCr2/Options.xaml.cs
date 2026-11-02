using System.ComponentModel.Composition;
using System.Windows;

namespace FitsMetadataForCr2 {

    [Export(typeof(ResourceDictionary))]
    public partial class Options : ResourceDictionary {

        public Options() {
            InitializeComponent();
        }
    }
}
