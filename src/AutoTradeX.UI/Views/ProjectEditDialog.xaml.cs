using System.Windows;

namespace AutoTradeX.UI.Views;

public partial class ProjectEditDialog : Window
{
    public string ProjectName { get; private set; } = "";
    public string ProjectDescription { get; private set; } = "";

    public ProjectEditDialog(string name = "", string description = "")
    {
        InitializeComponent();

        if (!string.IsNullOrEmpty(name))
        {
            DialogTitle.Text = "Edit Project";
            SaveButton.Content = "Save Changes";
            ProjectNameInput.Text = name;
            ProjectDescriptionInput.Text = description;
        }

        ProjectNameInput.Focus();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProjectNameInput.Text))
        {
            MessageBox.Show("Please enter a project name.", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ProjectName = ProjectNameInput.Text.Trim();
        ProjectDescription = ProjectDescriptionInput.Text.Trim();

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
