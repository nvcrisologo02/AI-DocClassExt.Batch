using System.Windows;
using DocumentIA.Batch.ClassificationLite.Models;

namespace DocumentIA.Batch.ClassificationLite.Views;

public partial class ConfigDialog : Window
{
    public ConfigDialog(LiteConfig config)
    {
        InitializeComponent();
        DataContext = config;
    }
}
