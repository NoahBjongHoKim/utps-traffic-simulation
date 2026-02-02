using System.Windows;

namespace UTPS_Addin
{
    /// <summary>
    /// Interaction logic for TrafficConfigDialog.xaml
    /// Dialog for configuring traffic data import parameters.
    /// </summary>
    public partial class TrafficConfigDialog : Window
    {
        public TrafficConfigDialog()
        {
            InitializeComponent();

            // Set the ViewModel as DataContext
            DataContext = new TrafficConfigViewModel(this);
        }
    }
}