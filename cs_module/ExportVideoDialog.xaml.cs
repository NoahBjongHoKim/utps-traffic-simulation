using System.Windows;

namespace UTPS_Addin
{
    /// <summary>
    /// Interaction logic for ExportVideoDialog.xaml
    /// Dialog for configuring video export parameters (output path, resolution,
    /// start/end second within the video's own timeline).
    /// </summary>
    public partial class ExportVideoDialog : Window
    {
        public ExportVideoDialog()
        {
            InitializeComponent();
            DataContext = new ExportVideoViewModel(this);
        }
    }
}
