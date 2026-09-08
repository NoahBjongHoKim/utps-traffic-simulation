using System.Windows;

namespace UTPS_Addin
{
    /// <summary>
    /// Minimal indeterminate-progress dialog shown while a video export runs via
    /// ArcGIS Pro's ViewAnimation.BeginExport, which provides no percent-complete
    /// signal — only a start bool and a completion event.
    /// </summary>
    public partial class ExportProgressDialog : Window
    {
        public ExportProgressDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Close this dialog with the given DialogResult. Safe to call from any
        /// thread — marshals to the UI thread via Dispatcher if needed.
        /// </summary>
        public void CloseWithResult(bool result)
        {
            if (Dispatcher.CheckAccess())
            {
                DialogResult = result;
                Close();
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    DialogResult = result;
                    Close();
                });
            }
        }
    }
}
